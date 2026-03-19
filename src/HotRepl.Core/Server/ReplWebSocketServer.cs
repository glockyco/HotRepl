using System;
using Fleck;

namespace HotRepl.Server;

/// <summary>
/// Thin wrapper around Fleck's WebSocketServer.
/// Owns the listener socket; raises typed events for connection lifecycle and messages.
/// Callbacks fire on Fleck's thread pool — callers must not do blocking work here.
/// </summary>
internal sealed class ReplWebSocketServer : IDisposable
{
    private readonly Action<string> _log;
    private Fleck.WebSocketServer? _server;

    /// <summary>Fires on Fleck thread when a client successfully opens a connection.</summary>
    public event Action<Guid, IWebSocketConnection>? ClientConnected;

    /// <summary>Fires on Fleck thread when a client disconnects or errors out.</summary>
    public event Action<Guid>? ClientDisconnected;

    /// <summary>Fires on Fleck thread for each inbound text frame.</summary>
    public event Action<Guid, string>? MessageReceived;

    public ReplWebSocketServer(Action<string> log) => _log = log;

    public void Start(int port)
    {
        var location = $"ws://0.0.0.0:{port}";

        // Redirect Fleck's internal logger through our host logger
        // instead of letting it hit Console.WriteLine (which BepInEx intercepts).
        // Debug-level Fleck output is suppressed — it's high-volume startup noise.
        Fleck.FleckLog.LogAction = (level, msg, ex) =>
        {
            if (level == LogLevel.Debug)
                return;
            _log($"[Fleck:{level}] {msg}{(ex == null ? "" : " -- " + ex.Message)}");
        };

        _server = new Fleck.WebSocketServer(location);
        _log($"[HotRepl] Calling Fleck Start()...");
        _server.Start(ConfigureSocket);
        _log($"[HotRepl] Fleck Start() returned. Server listening on {location}");
    }

    /// <summary>
    /// Sends <paramref name="json"/> to <paramref name="socket"/> if it is still available.
    /// Fire-and-forget; any send failure is logged.
    /// </summary>
    public void Send(IWebSocketConnection socket, string json)
    {
        if (!socket.IsAvailable)
            return;
        try
        { socket.Send(json); }
        catch (Exception ex) { _log($"[HotRepl] Send failed: {ex.Message}"); }
    }

    public void Dispose() => _server?.Dispose();

    // ── Private ───────────────────────────────────────────────────────────────

    private void ConfigureSocket(IWebSocketConnection socket)
    {
        var id = socket.ConnectionInfo.Id;

        socket.OnOpen = () => ClientConnected?.Invoke(id, socket);
        socket.OnClose = () => ClientDisconnected?.Invoke(id);
        socket.OnMessage = raw => MessageReceived?.Invoke(id, raw);
        socket.OnError = ex =>
        {
            _log($"[HotRepl] Socket error ({id}): {ex.Message}");
            ClientDisconnected?.Invoke(id);
        };
    }
}
