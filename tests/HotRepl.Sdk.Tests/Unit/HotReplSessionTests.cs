using System;
using System.Threading;
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

    [Fact]
    public async Task StartJobAsync_WaitsForTerminalResult()
    {
        await using var channel = new FakeFrameChannel();
        await using var session = CreateSession(channel);
        var pending = session.StartJobAsync<EchoArgs, EchoOutput>(
            "test.export",
            new EchoArgs { Text = "go" }
        );

        await channel.WaitForSentCountAsync(1);
        channel.EnqueueIncoming(
            "{\"type\":\"job_accepted\",\"id\":\"job-1\",\"jobId\":\"job-123\",\"state\":\"running\"}"
        );

        var job = await pending;
        var completion = job.WaitForCompletionAsync(TimeSpan.Zero, CancellationToken.None);

        await channel.WaitForSentCountAsync(2);
        channel.EnqueueIncoming(
            "{\"type\":\"job_result\",\"id\":\"status-2\",\"jobId\":\"job-123\",\"state\":\"done\",\"status\":\"ok\",\"output\":{\"value\":99},\"artifacts\":{}}"
        );

        var result = await completion;

        Assert.Equal("job-123", job.Id);
        Assert.Equal(99, result.Output.Value);
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
