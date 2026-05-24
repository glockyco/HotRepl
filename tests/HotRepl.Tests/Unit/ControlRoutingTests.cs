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
using ArtifactRef = HotRepl.Control.Artifacts.ArtifactRef;

namespace HotRepl.Tests.Unit;

public class ControlRoutingTests
{
    [Fact]
    public void List_ReturnsCommandSummariesFromRegistry()
    {
        var registry = NewRegistry(r => r.Register(new EchoCommand()));
        var router = new ControlCommandRouter(registry);

        var result = router.List("list-1");

        Assert.Equal(MessageType.CommandsListResult, result.Type);
        Assert.Equal("list-1", result.Id);
        var summary = Assert.Single(result.Commands);
        Assert.Equal("archive.echo", summary.Name);
        Assert.Equal(1, summary.MajorVersion);
        Assert.Equal("sync", summary.Kind);
        Assert.False(summary.MutatesState);
    }

    [Fact]
    public void List_EmitsLowercaseKindStrings()
    {
        var registry = NewRegistry(r =>
        {
            r.Register(new EchoCommand());
            r.Register(new JobCommand());
        });
        var router = new ControlCommandRouter(registry);

        var result = router.List("list-1");

        Assert.Collection(
            result.Commands,
            sync => Assert.Equal("sync", sync.Kind),
            job => Assert.Equal("job", job.Kind)
        );
    }

    [Fact]
    public void Describe_ReturnsOneDescriptorByName()
    {
        var registry = NewRegistry(r => r.Register(new EchoCommand()));
        var router = new ControlCommandRouter(registry);

        var result = Assert.IsType<CommandDescribeResultMessage>(
            router.Describe(new CommandDescribeMessage { Id = "describe-1", Name = "archive.echo" })
        );

        Assert.Equal(MessageType.CommandDescribeResult, result.Type);
        Assert.Equal("describe-1", result.Id);
        Assert.Equal("archive.echo", result.Descriptor.Name);
        Assert.Equal(1, result.Descriptor.MajorVersion);
        Assert.Equal("sync", result.Descriptor.Kind);
        Assert.Equal("object", result.Descriptor.InputSchema["type"]!.Value<string>());
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsCommandError()
    {
        var router = new ControlCommandRouter(EmptyControlCommandRegistry.Instance);
        var message = new CommandCallMessage { Id = "cmd-1", Name = "missing.command" };

        var result = router.Execute(message);

        var error = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal(MessageType.CommandResult, error.Type);
        Assert.Equal("cmd-1", error.Id);
        Assert.Equal("failed", error.Status);
        Assert.Equal(ErrorKind.UnknownCommand, error.Error!.Kind);
        Assert.False(error.Error.Retryable);
    }

    [Fact]
    public void Execute_KnownSynchronousCommand_ReturnsCommandResult()
    {
        var registry = NewRegistry(r => r.Register(new EchoCommand()));
        var router = new ControlCommandRouter(registry);
        var message = new CommandCallMessage
        {
            Id = "cmd-1",
            Name = "archive.echo",
            Args = JObject.Parse("{\"Value\":\"ok\"}"),
            TimeoutMs = 5000,
        };

        var result = router.Execute(message);

        var ok = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal(MessageType.CommandResult, ok.Type);
        Assert.Equal("cmd-1", ok.Id);
        Assert.Equal("ok", ok.Status);
        Assert.Equal("ok", ok.Output!["Value"]!.Value<string>());
    }

    [Fact]
    public void Execute_AppliesConfiguredCommandOutputLimits()
    {
        var registry = NewRegistry(r => r.Register(new LargeOutputCommand()));
        var router = new ControlCommandRouter(
            registry,
            config: new ReplConfig { MaxResultLength = 16 }
        );

        var result = Assert.IsType<CommandResultMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.largeOutput" })
        );
        Assert.Equal("failed", result.Status);
        Assert.Equal("resultTooLarge", result.Error!.Code);
    }

    [Fact]
    public void Execute_MutatingCommandDoesNotRequireLease()
    {
        var registry = NewRegistry(r => r.Register(new MutatingEchoCommand()));
        var router = new ControlCommandRouter(registry);

        var result = router.Execute(
            new CommandCallMessage
            {
                Id = "cmd-1",
                Name = "archive.mutate",
                Args = JObject.Parse("{\"Value\":\"ok\"}"),
            },
            Guid.NewGuid()
        );

        var ok = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal("ok", ok.Output!["Value"]!.Value<string>());
    }

    [Fact]
    public void Execute_HandlerException_ReturnsInternalCommandError()
    {
        var registry = NewRegistry(r => r.Register(new ThrowingCommand()));
        var router = new ControlCommandRouter(registry);
        var message = new CommandCallMessage { Id = "cmd-1", Name = "archive.throw" };

        var result = router.Execute(message);

        var error = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal("failed", error.Status);
        Assert.Equal(ErrorKind.Internal, error.Error!.Kind);
        Assert.Equal("handlerException", error.Error.Code);
        Assert.False(error.Error.Retryable);
        Assert.Contains("boom", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JobCommand_ReturnsCommandAccepted()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var registry = NewRegistry(r => r.Register(new JobCommand()));
        var router = new ControlCommandRouter(registry, jobs: jobs);

        var result = router.Execute(
            new CommandCallMessage { Id = "cmd-1", Name = "archive.export" }
        );

        var accepted = Assert.IsType<JobAcceptedMessage>(result);
        Assert.Equal(MessageType.JobAccepted, accepted.Type);
        Assert.Equal("cmd-1", accepted.Id);
        Assert.Equal("running", accepted.State);
        Assert.Equal("running", jobs.GetStatus(accepted.JobId).State);
    }

    [Fact]
    public void JobStatus_ReturnsCurrentState()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var registry = NewRegistry(r => r.Register(new JobCommand()));
        var router = new ControlCommandRouter(registry, jobs: jobs);
        var accepted = Assert.IsType<JobAcceptedMessage>(
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
    public async Task JobStatus_AfterCompletion_ReturnsTerminalJobResult()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var registry = NewRegistry(r => r.Register(new JobCommand()));
        var router = new ControlCommandRouter(registry, jobs: jobs);
        var accepted = Assert.IsType<JobAcceptedMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.export" })
        );
        await router.RunJobAsync(accepted.JobId);

        var result = router.GetJobStatus(
            new JobStatusMessage { Id = "status-1", JobId = accepted.JobId },
            Guid.Empty
        );

        var ok = Assert.IsType<JobResultMessage>(result);
        Assert.Equal(MessageType.JobResult, ok.Type);
        Assert.Equal("done", ok.State);
        Assert.Equal("ok", ok.Status);
        Assert.Equal("done", ok.Output!["Value"]!.Value<string>());
    }

    [Fact]
    public async Task JobStatus_AfterCompletion_RecordsTerminalCommandJournalEntry()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var recorded = new List<ControlCommandJournalEntry>();
        var registry = NewRegistry(r => r.Register(new JobCommand()));
        var router = new ControlCommandRouter(registry, jobs: jobs, onCommandResult: recorded.Add);
        var accepted = Assert.IsType<JobAcceptedMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.export" })
        );
        await router.RunJobAsync(accepted.JobId);

        _ = router.GetJobStatus(
            new JobStatusMessage { Id = "status-1", JobId = accepted.JobId },
            Guid.Empty
        );

        var entry = Assert.Single(recorded);
        Assert.Equal("cmd-1", entry.Id);
        Assert.Equal("archive.export", entry.Name);
        Assert.True(entry.Success);
        Assert.Null(entry.ErrorKind);
    }

    [Fact]
    public async Task JobStatus_AppliesConfiguredTerminalOutputLimits()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var registry = NewRegistry(r => r.Register(new LargeJobCommand()));
        var router = new ControlCommandRouter(
            registry,
            jobs: jobs,
            config: new ReplConfig { MaxResultLength = 16 }
        );
        var accepted = Assert.IsType<JobAcceptedMessage>(
            router.Execute(new CommandCallMessage { Id = "cmd-1", Name = "archive.largeJob" })
        );
        await router.RunJobAsync(accepted.JobId);

        var result = Assert.IsType<JobResultMessage>(
            router.GetJobStatus(
                new JobStatusMessage { Id = "status-1", JobId = accepted.JobId },
                Guid.Empty
            )
        );

        Assert.Equal("failed", result.Status);
        Assert.Equal("resultTooLarge", result.Error!.Code);
    }

    [Fact]
    public void JobCancel_ReturnsAcknowledgement()
    {
        var jobs = new ControlJobManager(maxEventBuffer: 100);
        var registry = NewRegistry(r => r.Register(new JobCommand()));
        var router = new ControlCommandRouter(registry, jobs: jobs);
        var accepted = Assert.IsType<JobAcceptedMessage>(
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

    private static GlobalControlCommandRegistry NewRegistry(
        Action<GlobalControlCommandRegistry> configure
    )
    {
        var registry = new GlobalControlCommandRegistry();
        configure(registry);
        return registry;
    }

    private sealed class EchoArgs
    {
        public string Value { get; set; } = "";
    }

    private sealed class EchoResult
    {
        public string Value { get; set; } = "";
    }

    private sealed class EchoCommand : IControlCommandHandler<EchoArgs, EchoResult>
    {
        public string Name => "archive.echo";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext<EchoResult> context,
            EchoArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult.Ok(new EchoResult { Value = args.Value }));
    }

    private sealed class MutatingEchoCommand : IControlCommandHandler<EchoArgs, EchoResult>
    {
        public string Name => "archive.mutate";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool MutatesState => true;

        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext<EchoResult> context,
            EchoArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult.Ok(new EchoResult { Value = args.Value }));
    }

    private sealed class ThrowingCommand : IControlCommandHandler<EmptyArgs, EchoResult>
    {
        public string Name => "archive.throw";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext<EchoResult> context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("boom");
    }

    private sealed class LargeOutputCommand : IControlCommandHandler<EmptyArgs, EchoResult>
    {
        public string Name => "archive.largeOutput";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext<EchoResult> context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult.Ok(new EchoResult { Value = new string('x', 64) }));
    }

    private sealed class JobCommand : IControlCommandHandler<EmptyArgs, EchoResult>
    {
        public string Name => "archive.export";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Job;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext<EchoResult> context,
            EmptyArgs args,
            CancellationToken cancellationToken
        )
        {
            var artifact = new ArtifactRef(
                "items",
                "file:///tmp/items.json",
                "/tmp/items.json",
                "application/json",
                10,
                "sha",
                true
            );
            return new(
                ControlCommandResult.Ok(new EchoResult { Value = "done" }, "items", artifact)
            );
        }
    }

    private sealed class LargeJobCommand : IControlCommandHandler<EmptyArgs, EchoResult>
    {
        public string Name => "archive.largeJob";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Job;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext<EchoResult> context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult.Ok(new EchoResult { Value = new string('x', 64) }));
    }
}
