using System;
using System.Collections.Concurrent;
using Fleck;
using HotRepl.Protocol;
using HotRepl.Protocol.Serialization;

namespace HotRepl.Server;

/// <summary>
/// Tracks connected WebSocket clients and enforces the single-client model:
/// when a second client connects, the previous one is evicted and closed.
///
/// SendTo() delivers only to the addressed connection and drops the message when
/// that connection is gone, so a replacement never receives another session's
/// responses. Send() targets the active client for unsolicited notifications.
///
/// All methods are called from Fleck's thread pool (Add/Remove) or from the main
/// thread (Send). ConcurrentDictionary makes cross-thread reads safe; the volatile
/// _current field makes the latest connection immediately visible to the main thread.
/// </summary>
internal sealed class ClientRegistry
{
    private readonly ConcurrentDictionary<Guid, IWebSocketConnection> _clients = new();
    private readonly Action<IWebSocketConnection, string> _send;
    private readonly Action<string> _log;

    private volatile IWebSocketConnection? _current;

    public ClientRegistry(ReplWebSocketServer server, Action<string> log)
        : this((socket, json) => server.Send(socket, json), log) { }

    internal ClientRegistry(Action<IWebSocketConnection, string> send, Action<string> log)
    {
        _send = send;
        _log = log;
    }

    public void OnConnected(Guid id, IWebSocketConnection socket)
    {
        // Single-client: forcibly close any previous connection.
        var prev = _current;
        if (prev != null && prev != socket)
        {
            // Deregister before the replacement registers. The engine drops a session's
            // queued work once it is unregistered, so the displaced session loses its
            // authority now rather than whenever Fleck reports the close.
            _clients.TryRemove(prev.ConnectionInfo.Id, out _);
            _log("[HotRepl] New client connected; closing previous connection.");
            try
            {
                _send(
                    prev,
                    ProtocolMessageSerializer.Serialize(
                        new SessionEvictedMessage
                        {
                            Reason = "displaced",
                            By = new SessionEvictedBy { ClientName = null },
                        }
                    )
                );
                prev.Close();
            }
            catch
            { /* best-effort */
            }
        }

        _clients[id] = socket;
        _current = socket;
        _log($"[HotRepl] Client connected: {socket.ConnectionInfo.ClientIpAddress}");
    }

    public void OnDisconnected(Guid id)
    {
        if (_clients.TryRemove(id, out var socket))
        {
            if (_current == socket)
                _current = null;
            _log($"[HotRepl] Client disconnected ({id}).");
        }
    }

    /// <summary>Whether the connection is registered, that is neither closed nor displaced.</summary>
    public bool IsConnected(Guid connectionId) => _clients.ContainsKey(connectionId);

    /// <summary>
    /// Sends to the current client. No-op if no client is connected.
    /// May be called from any thread (the underlying Send is on the main thread
    /// during ReplEngine message dispatch).
    /// </summary>
    public void Send(string json)
    {
        var client = _current;
        if (client == null)
            return;
        _send(client, json);
    }

    /// <summary>
    /// Sends a response only to its originating connection.
    /// Returns false when the connection no longer exists; never falls back to
    /// the replacement client because that would leak another session's result.
    /// </summary>
    public bool SendTo(Guid connectionId, string json)
    {
        if (!_clients.TryGetValue(connectionId, out var socket))
            return false;

        _send(socket, json);
        return true;
    }
}
