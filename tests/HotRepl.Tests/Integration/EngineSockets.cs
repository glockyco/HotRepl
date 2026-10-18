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
/// Drives a started engine through real WebSocket clients. The test thread calls Tick() the way
/// a host's frame loop would, and generated code records its runs in process environment
/// variables so tests can tell whether the engine ran it.
/// </summary>
internal static class EngineSockets
{
    public static string NewMarker() => "HOTREPL_TEST_" + Guid.NewGuid().ToString("N");

    // Records in a process environment variable that the code ran, then evaluates to result.
    public static string RecordRun(string marker, string result) =>
        "((System.Func<object>)(() => { System.Environment.SetEnvironmentVariable(\""
        + marker
        + "\", \"ran\"); return "
        + result
        + "; }))()";

    // Counts its evaluations in a process environment variable.
    public static string CountRuns(string marker) =>
        "((System.Func<int>)(() => { var runs = int.Parse(System.Environment.GetEnvironmentVariable(\""
        + marker
        + "\") ?? \"0\") + 1; System.Environment.SetEnvironmentVariable(\""
        + marker
        + "\", runs.ToString()); return runs; }))()";

    public static string Message(string type, string id) =>
        new JObject { ["type"] = type, ["id"] = id }.ToString(Formatting.None);

    public static string Eval(string id, string code) =>
        new JObject
        {
            ["type"] = "eval",
            ["id"] = id,
            ["code"] = code,
        }.ToString(Formatting.None);

    public static string Subscribe(string id, string code) =>
        new JObject
        {
            ["type"] = "subscribe",
            ["id"] = id,
            ["code"] = code,
            ["intervalFrames"] = 1,
        }.ToString(Formatting.None);

    public static string Cancel(string id, string targetId) =>
        new JObject
        {
            ["type"] = "cancel",
            ["id"] = id,
            ["targetId"] = targetId,
        }.ToString(Formatting.None);

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<ClientWebSocket> ConnectAsync(int port, CancellationToken token)
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

    public static Task SendAsync(ClientWebSocket client, string json, CancellationToken token) =>
        client.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            token
        );

    public static async Task<JObject> ReceiveAsync(ClientWebSocket client, CancellationToken token)
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
    public static async Task WaitForQueuedAsync(
        ReplEngine engine,
        int count,
        CancellationToken token
    )
    {
        while (engine.QueuedCommandCount < count)
            await Task.Delay(10, token);
    }

    public static async Task<JObject> TickUntilReceivedAsync(
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
