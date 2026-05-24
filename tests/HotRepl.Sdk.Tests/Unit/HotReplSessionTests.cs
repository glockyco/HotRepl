using System.Threading.Tasks;
using HotRepl.Sdk.Internal;
using HotRepl.Sdk.Tests.Fakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Sdk.Tests.Unit;

public sealed class HotReplSessionTests
{
    [Fact]
    public async Task ListCommandsAsync_CachesAcrossCalls()
    {
        await using var channel = new FakeFrameChannel();
        await using var session = CreateSession(channel);
        var pending = session.ListCommandsAsync();
        await channel.WaitForSentCountAsync(1);
        channel.EnqueueIncoming(
            "{\"type\":\"commands_list_result\",\"id\":\"list-1\",\"commands\":[{\"name\":\"unity.app.info\",\"majorVersion\":1,\"kind\":\"sync\",\"mutatesState\":false}]}"
        );

        var first = await pending;
        var second = await session.ListCommandsAsync();

        Assert.Same(first, second);
        Assert.Single(channel.Sent);
    }

    [Fact]
    public async Task RunAsync_ReturnsTypedOutputAndArtifacts()
    {
        await using var channel = new FakeFrameChannel();
        await using var session = CreateSession(channel);
        var pending = session.RunAsync<EchoArgs, EchoOutput>(
            "test.echo",
            new EchoArgs { Text = "hello" }
        );
        await channel.WaitForSentCountAsync(1);
        channel.EnqueueIncoming(
            "{\"type\":\"commands_list_result\",\"id\":\"list-1\",\"commands\":[{\"name\":\"test.echo\",\"majorVersion\":1,\"kind\":\"sync\",\"mutatesState\":false}]}"
        );
        await channel.WaitForSentCountAsync(2);
        channel.EnqueueIncoming(
            "{\"type\":\"command_result\",\"id\":\"run-2\",\"status\":\"ok\",\"output\":{\"value\":42},\"artifacts\":{\"data\":{\"uri\":\"file:///tmp/data.json\",\"path\":\"/tmp/data.json\",\"sha256\":\"abc\",\"byteSize\":12,\"contentType\":\"application/json\",\"finalized\":true}}}"
        );

        var result = await pending;

        Assert.Equal(42, result.Output.Value);
        Assert.True(result.Artifacts.ContainsKey("data"));
    }

    private static HotReplSession CreateSession(FakeFrameChannel channel)
    {
        var caps = new HotReplCapabilities(
            new JObject(),
            protocolVersion: 2,
            schemaValidation: true
        );
        return new HotReplSession(new MessageDispatcher(channel), caps, new HotReplClientOptions());
    }

    private sealed class EchoArgs
    {
        public string Text { get; set; } = "";
    }

    private sealed class EchoOutput
    {
        public int Value { get; set; }
    }
}
