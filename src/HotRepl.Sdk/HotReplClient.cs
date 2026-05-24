using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Sdk.Internal;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Entry point for connecting to a HotRepl runtime.</summary>
public sealed class HotReplClient
{
    private readonly Uri _endpoint;
    private readonly HotReplClientOptions _options;

    /// <summary>Create a client for one HotRepl WebSocket endpoint.</summary>
    public HotReplClient(Uri endpoint, HotReplClientOptions? options = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _options = options ?? new HotReplClientOptions();
    }

    /// <summary>Connect and read the server handshake.</summary>
    public async Task<HotReplSession> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ConnectTimeout);

        try
        {
            await socket.ConnectAsync(_endpoint, timeout.Token).ConfigureAwait(false);
            var channel = new WebSocketFrameChannel(socket);
            var raw =
                await channel.ReceiveAsync(timeout.Token).ConfigureAwait(false)
                ?? throw new HotReplProtocolException(
                    "noHandshake",
                    "Server closed before handshake."
                );
            var handshake = JObject.Parse(raw);
            if (!string.Equals((string?)handshake["type"], "handshake", StringComparison.Ordinal))
            {
                throw new HotReplProtocolException(
                    "expectedHandshake",
                    $"Expected handshake, got '{handshake["type"]}'."
                );
            }

            var capabilities = new HotReplCapabilities(
                handshake,
                (int?)handshake["protocolVersion"] ?? 0,
                (bool?)handshake.SelectToken("control.schemaValidation")
                    ?? (bool?)handshake.SelectToken("controlCapabilities.schemaValidation")
                    ?? false
            );
            return new HotReplSession(new MessageDispatcher(channel), capabilities, _options);
        }
        catch (HotReplException)
        {
            socket.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            throw new HotReplConnectionException(
                $"Failed to connect to {_endpoint}: {ex.Message}",
                ex
            );
        }
    }
}
