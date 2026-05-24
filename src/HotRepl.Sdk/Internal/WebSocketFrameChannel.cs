using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Sdk.Internal;

internal sealed class WebSocketFrameChannel : IDuplexFrameChannel
{
    private readonly ClientWebSocket _socket;

    public WebSocketFrameChannel(ClientWebSocket socket)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await _socket
                .ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                .ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new HotReplProtocolException(
                    "nonTextFrame",
                    "Expected text WebSocket frame."
                );
            }

            memory.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    public async Task SendAsync(string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket
            .SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return default;
    }
}
