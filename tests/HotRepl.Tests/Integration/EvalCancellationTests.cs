using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using static HotRepl.Tests.Integration.EngineSockets;

namespace HotRepl.Tests.Integration;

[Collection("Engine reload")]
public sealed class EvalCancellationTests
{
    [Fact]
    public async Task CancelledQueuedEval_StaysCancelledWhileAnEarlierEvalRuns()
    {
        var marker = NewMarker();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = timeout.Token;
            var port = FreePort();
            using var engine = new ReplEngine(new RoslynEngineHost(port));
            engine.Start();
            using var client = await ConnectAsync(port, token);

            await SendAsync(client, Eval("eval-1", "\"first\""), token);
            await SendAsync(client, Eval("eval-2", RecordRun(marker, "\"second\"")), token);
            await SendAsync(client, Cancel("cancel-1", "eval-2"), token);
            // Fleck handles one connection's messages in order, so the cancel has been applied
            // once this query is queued.
            await SendAsync(client, Message("journal_query", "journal-1"), token);
            await WaitForQueuedAsync(engine, 3, token);

            // Each Tick runs at most one eval, so eval-2 is still queued after the Tick that
            // runs eval-1.
            var replies = new Dictionary<string, JObject>(StringComparer.Ordinal);
            while (replies.Count < 3)
            {
                var reply = await TickUntilReceivedAsync(engine, client, token);
                replies[(string)reply["id"]!] = reply;
            }

            Assert.Equal("eval_result", (string?)replies["eval-1"]["type"]);
            Assert.Equal("eval_error", (string?)replies["eval-2"]["type"]);
            Assert.Equal("evalCancelled", (string?)replies["eval-2"]["error"]?["code"]);
            Assert.Null(Environment.GetEnvironmentVariable(marker));
        }
        finally
        {
            Environment.SetEnvironmentVariable(marker, null);
        }
    }

    [Fact]
    public async Task CancelFromAnotherConnection_LeavesEvalRunning()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var port = FreePort();
        using var engine = new ReplEngine(new RoslynEngineHost(port));
        engine.Start();
        using var client = await ConnectAsync(port, token);
        await SendAsync(client, Eval("eval-1", "\"kept\""), token);
        await WaitForQueuedAsync(engine, 1, token);

        // A displaced session's late cancel arrives with its own connection, and HotRepl's SDKs
        // number requests from one in every session.
        engine.CancelEval(Guid.NewGuid(), "eval-1");
        var reply = await TickUntilReceivedAsync(engine, client, token);

        Assert.Equal("eval_result", (string?)reply["type"]);
        Assert.Equal("kept", (string?)reply["value"]);
    }
}
