using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Jobs;
using HotRepl.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlRoutingTests
{
    [Fact]
    public void Describe_ReturnsDescriptorsFromRegistry()
    {
        var router = new ControlCommandRouter(new FakeRegistry(new EchoHandler()));

        var result = router.Describe("describe-1");

        Assert.Equal(MessageType.CommandDescribeResult, result.Type);
        Assert.Equal("describe-1", result.Id);
        var descriptor = Assert.Single(result.Commands);
        Assert.Equal("archive.echo", descriptor.Name);
        Assert.Equal(1, descriptor.Version);
        Assert.Equal("sync", descriptor.Kind);
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsCommandError()
    {
        var router = new ControlCommandRouter(EmptyControlCommandRegistry.Instance);
        var message = new CommandCallMessage { Id = "cmd-1", Name = "missing.command" };

        var result = router.Execute(message);

        var error = Assert.IsType<CommandErrorMessage>(result);
        Assert.Equal(MessageType.CommandError, error.Type);
        Assert.Equal("cmd-1", error.Id);
        Assert.Equal("failed", error.Status);
        Assert.Equal("unknown_command", error.Error.Kind);
        Assert.False(error.Error.Retryable);
    }

    [Fact]
    public void Execute_KnownSynchronousCommand_ReturnsCommandResult()
    {
        var router = new ControlCommandRouter(new FakeRegistry(new EchoHandler()));
        var message = new CommandCallMessage
        {
            Id = "cmd-1",
            Name = "archive.echo",
            Args = JObject.Parse("{\"value\":\"ok\"}"),
            TimeoutMs = 5000,
            IdempotencyKey = "run/echo/1",
        };

        var result = router.Execute(message);

        var ok = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal(MessageType.CommandResult, ok.Type);
        Assert.Equal("cmd-1", ok.Id);
        Assert.Equal("ok", ok.Status);
        Assert.Equal("ok", ok.Result["value"]!.Value<string>());
    }

    [Fact]
    public void Execute_MutatingCommandDoesNotRequireLease()
    {
        var router = new ControlCommandRouter(new FakeRegistry(new MutatingEchoHandler()));
        var result = router.Execute(
            new CommandCallMessage
            {
                Id = "cmd-1",
                Name = "archive.mutate",
                Args = JObject.Parse("{\"value\":\"ok\"}"),
            },
            Guid.NewGuid()
        );

        var ok = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal("ok", ok.Result["value"]!.Value<string>());
    }

    [Fact]
    public void Execute_MutatingCommandIgnoresLegacyLeaseFields()
    {
        var sessions = new ControlSessionManager(new ReplConfig { RequireControlLease = true });
        var ownerConnection = Guid.NewGuid();
        var attackerConnection = Guid.NewGuid();
        var auth = sessions.Authenticate(ownerConnection, token: null);
        var lease = sessions.AcquireLease(ownerConnection, auth.SessionId!, "owner");
        var router = new ControlCommandRouter(new FakeRegistry(new MutatingEchoHandler()), sessions);
        var result = router.Execute(
            new CommandCallMessage
            {
                Id = "cmd-1",
                Name = "archive.mutate",
                LeaseId = lease.LeaseId,
                Args = JObject.Parse("{\"value\":\"ok\"}"),
            },
            attackerConnection
        );

        var ok = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal("ok", ok.Result["value"]!.Value<string>());
    }

    [Fact]
    public void Execute_HandlerException_ReturnsInternalCommandError()
    {
        var router = new ControlCommandRouter(new FakeRegistry(new ThrowingHandler()));
        var message = new CommandCallMessage { Id = "cmd-1", Name = "archive.throw" };

        var result = router.Execute(message);

        var error = Assert.IsType<CommandErrorMessage>(result);
        Assert.Equal("failed", error.Status);
        Assert.Equal("internal", error.Error.Kind);
        Assert.Equal("handlerException", error.Error.Code);
        Assert.False(error.Error.Retryable);
        Assert.Contains("boom", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JobCommand_ReturnsCommandAccepted()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var router = new ControlCommandRouter(new FakeRegistry(new JobHandler()), jobs: jobs);

        var result = router.Execute(
            new CommandCallMessage { Id = "cmd-1", Name = "archive.export" }
        );

        var accepted = Assert.IsType<CommandAcceptedMessage>(result);
        Assert.Equal(MessageType.CommandAccepted, accepted.Type);
        Assert.Equal("cmd-1", accepted.Id);
        Assert.Equal("running", accepted.State);
        Assert.Equal("running", jobs.GetStatus(accepted.JobId).State);
    }

    [Fact]
    public void JobStatus_ReturnsCurrentState()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var router = new ControlCommandRouter(new FakeRegistry(new JobHandler()), jobs: jobs);
        var accepted = Assert.IsType<CommandAcceptedMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.export" })
        );

        var status = router.GetJobStatus(
            new JobStatusMessage { Id = "status-1", JobId = accepted.JobId }
        );

        Assert.Equal(MessageType.JobStatusResult, status.Type);
        Assert.Equal("status-1", status.Id);
        Assert.Equal(accepted.JobId, status.JobId);
        Assert.Equal("running", status.State);
    }

    [Fact]
    public void JobResult_BeforeTerminalState_ReturnsBusyError()
    {
        var router = new ControlCommandRouter(
            new FakeRegistry(new JobHandler()),
            jobs: new ControlJobManager(maxEventBuffer: 100)
        );
        var accepted = Assert.IsType<CommandAcceptedMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.export" })
        );

        var result = router.GetJobResult(
            new JobResultRequestMessage { Id = "result-1", JobId = accepted.JobId }
        );

        var error = Assert.IsType<CommandErrorMessage>(result);
        Assert.Equal("busy", error.Error.Kind);
        Assert.True(error.Error.Retryable);
    }

    [Fact]
    public async Task JobResult_AfterCompletion_ReturnsArtifactsAndResult()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var router = new ControlCommandRouter(new FakeRegistry(new JobHandler()), jobs: jobs);
        var accepted = Assert.IsType<CommandAcceptedMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.export" })
        );
        await router.RunJobAsync(accepted.JobId);

        var result = router.GetJobResult(
            new JobResultRequestMessage { Id = "result-1", JobId = accepted.JobId }
        );

        var ok = Assert.IsType<JobResultMessage>(result);
        Assert.Equal(MessageType.JobResult, ok.Type);
        Assert.Equal("done", ok.State);
        Assert.Equal("ok", ok.Status);
        Assert.Equal("done", ok.Result["value"]!.Value<string>());
        var artifact = Assert.Single(ok.Artifacts);
        Assert.Equal("items", artifact.Key);
        Assert.Equal("sha", artifact.Value.Sha256);
    }

    [Fact]
    public void JobCancel_ReturnsAcknowledgement()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var router = new ControlCommandRouter(new FakeRegistry(new JobHandler()), jobs: jobs);
        var accepted = Assert.IsType<CommandAcceptedMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.export" })
        );

        var result = router.CancelJob(
            new JobCancelMessage { Id = "cancel-1", JobId = accepted.JobId }
        );

        Assert.Equal(MessageType.JobCancelResult, result.Type);
        Assert.Equal("cancel-1", result.Id);
        Assert.True(result.Accepted);
        Assert.Equal("running", result.State);
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

    private sealed class EchoHandler : IControlCommandHandler
    {
        public ControlCommandDescriptor Descriptor { get; } =
            new(
                "archive.echo",
                1,
                ControlCommandKind.Synchronous,
                mutatesState: false,
                argsSchema: JObject.Parse("{\"type\":\"object\"}"),
                resultSchema: JObject.Parse("{\"type\":\"object\"}")
            );

        public ValueTask<ControlCommandResult> ExecuteAsync(
            ControlCommandContext context,
            JObject args,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(
                new ControlCommandResult(
                    new JObject { ["value"] = args["value"]?.Value<string>() ?? "" },
                    Array.Empty<HotRepl.Control.Artifacts.ArtifactRef>(),
                    Array.Empty<ControlCommandError>()
                )
            );
        }
    }

    private sealed class ThrowingHandler : IControlCommandHandler
    {
        public ControlCommandDescriptor Descriptor { get; } =
            new(
                "archive.throw",
                1,
                ControlCommandKind.Synchronous,
                mutatesState: false,
                argsSchema: JObject.Parse("{\"type\":\"object\"}"),
                resultSchema: JObject.Parse("{\"type\":\"object\"}")
            );

        public ValueTask<ControlCommandResult> ExecuteAsync(
            ControlCommandContext context,
            JObject args,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("boom");
    }

    private sealed class MutatingEchoHandler : IControlCommandHandler
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
        )
        {
            return ValueTask.FromResult(
                new ControlCommandResult(
                    new JObject { ["value"] = args["value"]?.Value<string>() ?? "" },
                    Array.Empty<ArtifactRef>(),
                    Array.Empty<ControlCommandError>()
                )
            );
        }
    }

    private sealed class JobHandler : IControlCommandHandler
    {
        public ControlCommandDescriptor Descriptor { get; } =
            new(
                "archive.export",
                1,
                ControlCommandKind.Job,
                mutatesState: false,
                argsSchema: JObject.Parse("{\"type\":\"object\"}"),
                resultSchema: JObject.Parse("{\"type\":\"object\"}")
            );

        public ValueTask<ControlCommandResult> ExecuteAsync(
            ControlCommandContext context,
            JObject args,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(
                new ControlCommandResult(
                    new JObject { ["value"] = "done" },
                    new[]
                    {
                        new ArtifactRef(
                            "items",
                            "file:///tmp/items.json",
                            "/tmp/items.json",
                            "application/json",
                            10,
                            "sha",
                            true
                        ),
                    },
                    Array.Empty<ControlCommandError>()
                )
            );
        }
    }
}
