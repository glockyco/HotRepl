using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Protocol;
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
        var manager = new ControlSessionManager(new ReplConfig { ControlAuthToken = "secret", RequireControlAuth = true });

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
        public ControlCommandDescriptor Descriptor { get; } = new(
            "archive.mutate",
            1,
            ControlCommandKind.Synchronous,
            mutatesState: true,
            argsSchema: JObject.Parse("{\"type\":\"object\"}"),
            resultSchema: JObject.Parse("{\"type\":\"object\"}"));

        public ValueTask<ControlCommandResult> ExecuteAsync(ControlCommandContext context, JObject args, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ControlCommandResult.Empty);
    }
}
