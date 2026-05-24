using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Sdk.Internal;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Sdk.Tests.Unit;

public sealed class MessageDispatcherTests
{
    [Fact]
    public async Task ExpectResponseAsync_CompletesMatchingRequestById()
    {
        await using var channel = new FakeFrameChannel();
        await using var dispatcher = new MessageDispatcher(channel);

        var pending = dispatcher.ExpectResponseAsync(
            "req-1",
            TimeSpan.FromSeconds(1),
            CancellationToken.None
        );
        channel.EnqueueIncoming("{\"type\":\"command_result\",\"id\":\"req-1\",\"status\":\"ok\"}");

        var response = await pending;

        Assert.Equal("command_result", response["type"]!.ToString());
        Assert.Equal("req-1", response["id"]!.ToString());
    }

    private sealed class FakeFrameChannel : IDuplexFrameChannel
    {
        private readonly Queue<string?> _incoming = new();
        private readonly SemaphoreSlim _signal = new(0);
        private bool _disposed;

        public List<string> Sent { get; } = new();

        public void EnqueueIncoming(string? json)
        {
            _incoming.Enqueue(json);
            _signal.Release();
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            return _incoming.Dequeue();
        }

        public Task SendAsync(string json, CancellationToken cancellationToken)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _signal.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
