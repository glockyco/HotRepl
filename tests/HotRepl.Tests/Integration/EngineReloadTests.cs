using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using HotRepl.Control;
using HotRepl.Evaluator;
using HotRepl.Evaluator.MonoCSharp;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Integration;

public sealed class EngineReloadTests
{
    [Fact]
    public async Task Dispose_AllowsNewEngineToConnectOnSamePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var originalLogAction = FleckLog.LogAction;
        var originalOut = Console.Out;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = new ReloadHost(port);

        using (var first = new ReplEngine(host))
        using (var client = new ClientWebSocket())
        {
            first.Start();
            await ConnectAndReadHandshake(client, port, timeout.Token);
            first.Dispose();
            Assert.Same(originalLogAction, FleckLog.LogAction);
            Assert.Same(originalOut, Console.Out);
            var closed = await client.ReceiveAsync(
                new ArraySegment<byte>(new byte[4096]),
                timeout.Token
            );
            Assert.Equal(WebSocketMessageType.Close, closed.MessageType);
        }

        using (var second = new ReplEngine(host))
        using (var client = new ClientWebSocket())
        {
            second.Start();
            await ConnectAndReadHandshake(client, port, timeout.Token);
        }

        Assert.Same(originalLogAction, FleckLog.LogAction);
        Assert.Same(originalOut, Console.Out);
    }

    private static async Task ConnectAndReadHandshake(
        ClientWebSocket client,
        int port,
        CancellationToken token
    )
    {
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), token);
        var buffer = new byte[65536];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        var handshake = JObject.Parse(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
        Assert.Equal("handshake", handshake["type"]!.Value<string>());
        Assert.Equal("Mono.CSharp", handshake["evaluator"]!["name"]!.Value<string>());
    }

    private sealed class ReloadHost : IReplHost
    {
        public ReloadHost(int port) => Config.Port = port;

        public ReplConfig Config { get; } = new();
        public HostInfo HostInfo { get; } = new() { Name = "Reload test" };
        public IControlCommandRegistry ControlCommands => EmptyControlCommandRegistry.Instance;
        public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators { get; } =
            new[] { MonoCSharpEvaluator.MonoCapabilities };
        public string DefaultEvaluatorName => MonoCSharpEvaluator.MonoCapabilities.Name;
        public IReadOnlyList<Assembly> AdditionalAssemblies => Array.Empty<Assembly>();
        public IReadOnlyList<string> AdditionalUsings => Array.Empty<string>();
        public string[] AdditionalHelperSignatures => Array.Empty<string>();

        public ICodeEvaluator CreateEvaluator(string evaluatorName) =>
            new MonoCSharpEvaluator(this);

        public void LogInfo(string message) { }

        public void LogDebug(string message) { }

        public void LogWarning(string message) { }

        public void LogError(string message, Exception? ex = null) { }
    }
}
