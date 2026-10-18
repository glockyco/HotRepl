using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

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

    private static string NewMarker() => "HOTREPL_TEST_" + Guid.NewGuid().ToString("N");

    // Records in a process environment variable that the code ran, then evaluates to result.
    private static string RecordRun(string marker, string result) =>
        "((System.Func<object>)(() => { System.Environment.SetEnvironmentVariable(\""
        + marker
        + "\", \"ran\"); return "
        + result
        + "; }))()";

    // Counts its evaluations in a process environment variable.
    private static string CountRuns(string marker) =>
        "((System.Func<int>)(() => { var runs = int.Parse(System.Environment.GetEnvironmentVariable(\""
        + marker
        + "\") ?? \"0\") + 1; System.Environment.SetEnvironmentVariable(\""
        + marker
        + "\", runs.ToString()); return runs; }))()";

    private static string Message(string type, string id) =>
        new JObject { ["type"] = type, ["id"] = id }.ToString(Formatting.None);

    private static string Eval(string id, string code) =>
        new JObject
        {
            ["type"] = "eval",
            ["id"] = id,
            ["code"] = code,
        }.ToString(Formatting.None);

    private static string Subscribe(string id, string code) =>
        new JObject
        {
            ["type"] = "subscribe",
            ["id"] = id,
            ["code"] = code,
            ["intervalFrames"] = 1,
        }.ToString(Formatting.None);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<ClientWebSocket> ConnectAsync(int port, CancellationToken token)
    {
        var client = new ClientWebSocket();
        try
        {
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), token);
            var handshake = await ReceiveAsync(client, token);
            Assert.Equal("handshake", (string?)handshake["type"]);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static Task SendAsync(ClientWebSocket client, string json, CancellationToken token) =>
        client.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            token
        );

    private static async Task<JObject> ReceiveAsync(ClientWebSocket client, CancellationToken token)
    {
        var buffer = new byte[65536];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JObject.Parse(Encoding.UTF8.GetString(message.ToArray()));
    }

    // Fleck parses and enqueues messages on its own threads.
    private static async Task WaitForQueuedAsync(
        ReplEngine engine,
        int count,
        CancellationToken token
    )
    {
        while (engine.QueuedCommandCount < count)
            await Task.Delay(10, token);
    }

    private static async Task<JObject> TickUntilReceivedAsync(
        ReplEngine engine,
        ClientWebSocket client,
        CancellationToken token
    )
    {
        var receive = ReceiveAsync(client, token);
        while (!receive.IsCompleted)
        {
            engine.Tick();
            await Task.Delay(10, token);
        }
        return await receive;
    }
}
