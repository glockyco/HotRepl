using System;
using Fleck;
using HotRepl.Protocol;

namespace HotRepl.Server
{
    /// <summary>
    /// Single-client Fleck WebSocket server that routes inbound messages by their
    /// protocol "type" field. On connect, sends a handshake JSON payload to the client.
    /// </summary>
    /// <remarks>
    /// Messages arrive on Fleck's thread pool. The <paramref name="onMessage"/> callback
    /// should enqueue work rather than executing it directly to avoid blocking the I/O thread.
    /// </remarks>
    internal sealed class ReplServer : IDisposable
    {
        private readonly int _port;
        private readonly string _handshakeJson;
        private readonly Action<string, string> _onMessage;
        private readonly Action<string> _log;

        private WebSocketServer? _server;
        private volatile IWebSocketConnection? _client;
        private bool _disposed;

        /// <summary>
        /// Creates a new REPL WebSocket server.
        /// </summary>
        /// <param name="port">TCP port to listen on.</param>
        /// <param name="handshakeJson">
        /// Pre-serialized handshake JSON sent to every client immediately on connect.
        /// </param>
        /// <param name="onMessage">
        /// Callback invoked with (messageType, rawJson) for every inbound message.
        /// Called on Fleck's I/O thread — must not block.
        /// </param>
        /// <param name="log">Optional log sink; defaults to <see cref="Console.WriteLine"/>.</param>
        public ReplServer(int port, string handshakeJson, Action<string, string> onMessage, Action<string>? log = null)
        {
            _port = port;
            _handshakeJson = handshakeJson;
            _onMessage = onMessage;
            _log = log ?? Console.WriteLine;
        }

        /// <summary>
        /// Starts listening for WebSocket connections. Safe to call only once.
        /// </summary>
        public void Start()
        {
            if (_server != null)
                throw new InvalidOperationException("Server is already started.");

            var location = $"ws://0.0.0.0:{_port}";

            try
            {
                _server = new WebSocketServer(location);
                _server.Start(ConfigureSocket);
                _log($"[HotRepl] WebSocket server started on {location}");
            }
            catch (Exception ex)
            {
                _log($"[HotRepl] Failed to start WebSocket server: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Sends a JSON message to the connected client. No-op if no client is connected.
        /// </summary>
        public void Send(string json)
        {
            var client = _client;
            if (client == null || !client.IsAvailable)
                return;

            try
            {
                client.Send(json);
            }
            catch (Exception ex)
            {
                _log($"[HotRepl] Failed to send to client: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _client?.Close();
            }
            catch
            {
                // Best-effort during shutdown.
            }

            _server?.Dispose();
            _server = null;
            _client = null;
            _disposed = true;

            _log("[HotRepl] WebSocket server stopped.");
        }

        private void ConfigureSocket(IWebSocketConnection socket)
        {
            socket.OnOpen = () =>
            {
                // Single-client model: replace any existing connection.
                var previous = _client;
                _client = socket;
                _log($"[HotRepl] Client connected: {socket.ConnectionInfo.ClientIpAddress}");

                if (previous != null && previous != socket)
                {
                    _log("[HotRepl] Replacing previous client connection.");
                    try
                    { previous.Close(); }
                    catch { /* best-effort */ }
                }

                // Send handshake immediately so the client knows our capabilities.
                try
                {
                    socket.Send(_handshakeJson);
                }
                catch (Exception ex)
                {
                    _log($"[HotRepl] Failed to send handshake: {ex.Message}");
                }
            };

            socket.OnClose = () =>
            {
                if (_client == socket)
                    _client = null;
                _log($"[HotRepl] Client disconnected: {socket.ConnectionInfo.ClientIpAddress}");
            };

            socket.OnError = ex =>
            {
                _log($"[HotRepl] Client error ({socket.ConnectionInfo.ClientIpAddress}): {ex.Message}");
                if (_client == socket)
                    _client = null;
            };

            socket.OnMessage = raw =>
            {
                try
                {
                    var type = MessageSerializer.ParseType(raw);
                    _onMessage(type, raw);
                }
                catch (Exception ex)
                {
                    _log($"[HotRepl] Failed to parse inbound message: {ex.Message}");
                }
            };
        }
    }
}
