using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using HotRepl.Control;
using HotRepl.Protocol;
using HotRepl.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlSessionManagerTests
{
    [Fact]
    public void Authenticate_SucceedsWhenNoTokenRequired()
    {
        var manager = new ControlSessionManager(new ReplConfig());

        var result = manager.Authenticate(Guid.NewGuid(), token: null);

        Assert.True(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionId));
    }

    [Fact]
    public void Authenticate_FailsForWrongConfiguredToken()
    {
        var manager = new ControlSessionManager(
            new ReplConfig { ControlAuthToken = "secret", RequireControlAuth = true }
        );

        var result = manager.Authenticate(Guid.NewGuid(), token: "wrong");

        Assert.False(result.Ok);
        Assert.Equal("auth_failed", result.Error!.Kind);
        Assert.False(result.Error.Retryable);
    }

    [Fact]
    public void AcquireLease_SucceedsForAuthenticatedSession()
    {
        var manager = new ControlSessionManager(new ReplConfig());
        var auth = manager.Authenticate(Guid.NewGuid(), token: null);

        var lease = manager.AcquireLease(auth.SessionId!, "ardenfall-export");

        Assert.True(lease.Ok);
        Assert.False(string.IsNullOrWhiteSpace(lease.LeaseId));
    }

    [Fact]
    public void AcquireLease_RejectsSecondActiveLease()
    {
        var manager = new ControlSessionManager(new ReplConfig());
        var first = manager.Authenticate(Guid.NewGuid(), token: null);
        var second = manager.Authenticate(Guid.NewGuid(), token: null);
        Assert.True(manager.AcquireLease(first.SessionId!, "first").Ok);

        var lease = manager.AcquireLease(second.SessionId!, "second");

        Assert.False(lease.Ok);
        Assert.Equal("lease_conflict", lease.Error!.Kind);
    }

    [Fact]
    public void OnDisconnected_ReleasesLeaseOwnedByConnection()
    {
        var manager = new ControlSessionManager(new ReplConfig());
        var firstConnection = Guid.NewGuid();
        var first = manager.Authenticate(firstConnection, token: null);
        var second = manager.Authenticate(Guid.NewGuid(), token: null);
        Assert.True(manager.AcquireLease(first.SessionId!, "first").Ok);

        manager.OnDisconnected(firstConnection);
        var lease = manager.AcquireLease(second.SessionId!, "second");

        Assert.True(lease.Ok);
    }

    [Fact]
    public void Router_RejectsMutatingCommandWithoutLease()
    {
        var manager = new ControlSessionManager(new ReplConfig { RequireControlLease = true });
        var router = new ControlCommandRouter(new FakeRegistry(new MutatingHandler()), manager);
        var message = new CommandCallMessage { Id = "cmd-1", Name = "archive.mutate" };

        var result = router.Execute(message);

        var error = Assert.IsType<CommandErrorMessage>(result);
        Assert.Equal("lease_required", error.Error.Kind);
        Assert.True(error.Error.Retryable);
    }

    [Fact]
    public void ServerLocation_DefaultsToLoopback()
    {
        var config = new ReplConfig();

        var location = ReplWebSocketServer.BuildLocation(config.Port, config.BindHost);

        Assert.Equal($"ws://127.0.0.1:{config.Port}", location);
    }

    [Fact]
    public void ServerLocation_HonorsExplicitBindHost()
    {
        var location = ReplWebSocketServer.BuildLocation(18590, "0.0.0.0");

        Assert.Equal("ws://0.0.0.0:18590", location);
    }

    [Fact]
    public void ControlResponseToDisconnectedClient_DoesNotFallbackToReplacementClient()
    {
        var sent = new List<string>();
        var registry = new ClientRegistry(
            (socket, json) => sent.Add($"{socket.ConnectionInfo.Id}:{json}"),
            _ => { }
        );
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new FakeSocket(firstId);
        var second = new FakeSocket(secondId);
        registry.OnConnected(firstId, first);
        registry.OnDisconnected(firstId);
        registry.OnConnected(secondId, second);

        var delivered = registry.SendControlTo(firstId, "{\"type\":\"command_result\"}");

        Assert.False(delivered);
        Assert.Empty(sent);
    }

    private sealed class FakeRegistry : IControlCommandRegistry
    {
        private readonly IControlCommandHandler _handler;

        public FakeRegistry(IControlCommandHandler handler) => _handler = handler;

        public IReadOnlyList<ControlCommandDescriptor> Describe() => new[] { _handler.Descriptor };

        public bool TryGet(string name, out IControlCommandHandler handler)
        {
            handler = _handler;
            return string.Equals(name, _handler.Descriptor.Name, StringComparison.Ordinal);
        }
    }

    private sealed class MutatingHandler : IControlCommandHandler
    {
        public ControlCommandDescriptor Descriptor { get; } =
            new(
                "archive.mutate",
                1,
                ControlCommandKind.Synchronous,
                mutatesState: true,
                argsSchema: JObject.Parse("{\"type\":\"object\"}"),
                resultSchema: JObject.Parse("{\"type\":\"object\"}")
            );

        public ValueTask<ControlCommandResult> ExecuteAsync(
            ControlCommandContext context,
            JObject args,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(ControlCommandResult.Empty);
    }

    private sealed class FakeSocket : IWebSocketConnection
    {
        public FakeSocket(Guid id) => ConnectionInfo = new FakeConnectionInfo(id);

        public Action OnOpen { get; set; } = () => { };
        public Action OnClose { get; set; } = () => { };
        public Action<string> OnMessage { get; set; } = _ => { };
        public Action<byte[]> OnBinary { get; set; } = _ => { };
        public Action<byte[]> OnPing { get; set; } = _ => { };
        public Action<byte[]> OnPong { get; set; } = _ => { };
        public Action<Exception> OnError { get; set; } = _ => { };
        public IWebSocketConnectionInfo ConnectionInfo { get; }
        public bool IsAvailable { get; set; } = true;

        public Task Send(string message) => Task.CompletedTask;

        public Task Send(byte[] message) => Task.CompletedTask;

        public Task SendPing(byte[] message) => Task.CompletedTask;

        public Task SendPong(byte[] message) => Task.CompletedTask;

        public void Close() => IsAvailable = false;

        public void Close(int code) => IsAvailable = false;
    }

    private sealed class FakeConnectionInfo : IWebSocketConnectionInfo
    {
        public FakeConnectionInfo(Guid id) => Id = id;

        public string SubProtocol => "";
        public string Origin => "";
        public string Host => "localhost";
        public string Path => "/";
        public string ClientIpAddress => "127.0.0.1";
        public int ClientPort => 12345;
        public IDictionary<string, string> Cookies { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public IDictionary<string, string> Headers { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public Guid Id { get; }
        public string NegotiatedSubProtocol => "";
    }
}
