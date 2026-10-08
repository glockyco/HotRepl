using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ConcurrentDictionary<Guid, IWebSocketConnection> _connections = new();
    private Action<LogLevel, string, Exception>? _previousLogAction;
    private Action<LogLevel, string, Exception>? _logAction;
    private volatile bool _disposed;

    /// <summary>Fires on Fleck thread when a client successfully opens a connection.</summary>
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;

    /// <summary>Fires on Fleck thread when a client disconnects or errors out.</summary>
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    /// <summary>Fires on Fleck thread for each inbound text frame.</summary>
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    public ReplWebSocketServer(Action<string> log) => _log = log;

    public void Start(int port) => Start(port, "127.0.0.1");

    public void Start(int port, string bindHost)
    {
        var location = BuildLocation(port, bindHost);

        // Redirect Fleck's internal logger through our host logger
        // instead of letting it hit Console.WriteLine (which BepInEx intercepts).
        // Debug-level Fleck output is suppressed — it's high-volume startup noise.
        _previousLogAction = FleckLog.LogAction;
        _logAction = (level, msg, ex) =>
        {
            if (level == LogLevel.Debug)
                return;
            _log($"[Fleck:{level}] {msg}{(ex == null ? "" : " -- " + ex.Message)}");
        };
        FleckLog.LogAction = _logAction;

        _server = new Fleck.WebSocketServer(location);
        _log($"[HotRepl] Calling Fleck Start()...");
        _server.Start(ConfigureSocket);
        _log($"[HotRepl] Fleck Start() returned. Server listening on {location}");
    }

    internal static string BuildLocation(int port, string? bindHost)
    {
        var host = string.IsNullOrWhiteSpace(bindHost) ? "127.0.0.1" : bindHost;
        return $"ws://{host}:{port}";
    }

    /// <summary>
    /// Sends <paramref name="json"/> to <paramref name="socket"/> if it is still available.
    /// Fire-and-forget; both synchronous and asynchronous send failures are logged.
    /// </summary>
    public void Send(IWebSocketConnection socket, string json)
    {
        if (!socket.IsAvailable)
            return;
        Task sendTask;
        try
        {
            sendTask = socket.Send(json);
        }
        catch (Exception ex)
        {
            _log($"[HotRepl] Send failed: {ex.Message}");
            return;
        }
        _ = sendTask.ContinueWith(
            t => _log($"[HotRepl] Send failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _server?.Dispose();
            foreach (var connection in _connections.Values)
                connection.Close();
            _connections.Clear();
        }
        finally
        {
            // Fleck lives in a shared library across plugin reloads. Do not retain
            // the unloaded host through its process-wide logging delegate.
            if (ReferenceEquals(FleckLog.LogAction, _logAction))
                FleckLog.LogAction = _previousLogAction;
            _logAction = null;
            _previousLogAction = null;
            ClientConnected = null;
            ClientDisconnected = null;
            MessageReceived = null;
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void ConfigureSocket(IWebSocketConnection socket)
    {
        var id = socket.ConnectionInfo.Id;
        _connections[id] = socket;
        if (_disposed)
        {
            _connections.TryRemove(id, out _);
            socket.Close();
            return;
        }

        socket.OnOpen = () =>
        {
            if (!_disposed)
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(id, socket));
        };
        socket.OnClose = () =>
        {
            _connections.TryRemove(id, out _);
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(id));
        };
        socket.OnMessage = raw =>
        {
            if (!_disposed)
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(id, raw));
        };
        socket.OnError = ex =>
        {
            _log($"[HotRepl] Socket error ({id}): {ex.Message}");
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(id));
        };
    }
}
