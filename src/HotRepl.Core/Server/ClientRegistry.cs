using System;
using System.Collections.Concurrent;
using Fleck;

namespace HotRepl.Server;

/// <summary>
/// Tracks connected WebSocket clients and enforces the single-client model:
/// when a second client connects, the previous one is closed.
///
/// Send() targets the current client regardless of connection ID, which means
/// responses always go to whoever is currently connected. If a client disconnects
/// between submitting an eval and receiving the result the send is silently dropped.
///
/// All methods are called from Fleck's thread pool (Add/Remove) or from the main
/// thread (Send). ConcurrentDictionary makes cross-thread reads safe; the volatile
/// _current field makes the latest connection immediately visible to the main thread.
/// </summary>
internal sealed class ClientRegistry
{
    private readonly ConcurrentDictionary<Guid, IWebSocketConnection> _clients = new();
    private readonly ReplWebSocketServer _server;
    private readonly Action<string> _log;

    private volatile IWebSocketConnection? _current;

    public ClientRegistry(ReplWebSocketServer server, Action<string> log)
    {
        _server = server;
        _log = log;
    }

    public void OnConnected(Guid id, IWebSocketConnection socket)
    {
        // Single-client: forcibly close any previous connection.
        var prev = _current;
        if (prev != null && prev != socket)
        {
            _log("[HotRepl] New client connected; closing previous connection.");
            try
            { prev.Close(); }
            catch { /* best-effort */ }
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
        _server.Send(client, json);
    }

    /// <summary>
    /// Sends to a specific connection by ID. Falls back to Send() if the ID is
    /// no longer present (client disconnected and reconnected between enqueue and send).
    /// </summary>
    public void SendTo(Guid connectionId, string json)
    {
        if (_clients.TryGetValue(connectionId, out var socket))
            _server.Send(socket, json);
        else
            Send(json); // best-effort delivery to current client
    }
}
