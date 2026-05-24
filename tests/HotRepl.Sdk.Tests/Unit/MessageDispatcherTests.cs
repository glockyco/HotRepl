using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Sdk.Internal;
using HotRepl.Sdk.Tests.Fakes;
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
}
