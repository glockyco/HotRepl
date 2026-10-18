using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static HotRepl.Tests.Integration.EngineSockets;

namespace HotRepl.Tests.Integration;

/// <summary>
/// A replacement client must never run or receive work that the session it displaced left
/// queued. HotRepl's SDKs number requests from one in every session, so displaced and
/// replacement requests share IDs.
/// </summary>
[Collection("Engine reload")]
public sealed class SessionIsolationTests
{
    [Fact]
    public async Task DisplacedSessionQueuedEval_NeitherRunsNorReachesReplacement()
    {
        var marker = NewMarker();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = timeout.Token;
            var port = FreePort();
            using var engine = new ReplEngine(new RoslynEngineHost(port));
            engine.Start();

            using var first = await ConnectAsync(port, token);
            await SendAsync(first, Eval("eval-1", RecordRun(marker, "\"first\"")), token);
            await WaitForQueuedAsync(engine, 1, token);
            using var second = await ConnectAsync(port, token);
            await SendAsync(second, Eval("eval-1", "\"second\""), token);
            await WaitForQueuedAsync(engine, 2, token);

            var reply = await TickUntilReceivedAsync(engine, second, token);

            Assert.Equal("eval_result", (string?)reply["type"]);
            Assert.Equal("eval-1", (string?)reply["id"]);
            Assert.Equal("second", (string?)reply["value"]);
            Assert.Null(Environment.GetEnvironmentVariable(marker));
        }
        finally
        {
            Environment.SetEnvironmentVariable(marker, null);
        }
    }

    [Fact]
    public async Task DisplacedSessionQueuedReset_DoesNotRun()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var port = FreePort();
        using var engine = new ReplEngine(new RoslynEngineHost(port));
        engine.Start();

        using var first = await ConnectAsync(port, token);
        await SendAsync(first, Message("reset", "reset-1"), token);
        await WaitForQueuedAsync(engine, 1, token);
        using var second = await ConnectAsync(port, token);
        await SendAsync(second, Message("journal_query", "journal-1"), token);
        await WaitForQueuedAsync(engine, 2, token);

        var reply = await TickUntilReceivedAsync(engine, second, token);

        Assert.Equal("journal_query_result", (string?)reply["type"]);
        Assert.DoesNotContain(
            reply["entries"]!,
            entry => string.Equals((string?)entry["id"], "reset-1", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task DisplacedSessionSubscription_StopsEvaluating()
    {
        var marker = NewMarker();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = timeout.Token;
            var port = FreePort();
            using var engine = new ReplEngine(new RoslynEngineHost(port));
            engine.Start();

            using var first = await ConnectAsync(port, token);
            await SendAsync(first, Subscribe("watch-1", CountRuns(marker)), token);
            await WaitForQueuedAsync(engine, 1, token);
            while (Environment.GetEnvironmentVariable(marker) == null)
            {
                engine.Tick();
                await Task.Delay(10, token);
            }
            var runsBeforeDisplacement = Environment.GetEnvironmentVariable(marker);

            using var second = await ConnectAsync(port, token);
            await SendAsync(second, Eval("eval-1", "\"second\""), token);
            await WaitForQueuedAsync(engine, 1, token);
            var reply = await TickUntilReceivedAsync(engine, second, token);
            for (var i = 0; i < 5; i++)
                engine.Tick();

            Assert.Equal("eval_result", (string?)reply["type"]);
            Assert.Equal(runsBeforeDisplacement, Environment.GetEnvironmentVariable(marker));
        }
        finally
        {
            Environment.SetEnvironmentVariable(marker, null);
        }
    }
}
