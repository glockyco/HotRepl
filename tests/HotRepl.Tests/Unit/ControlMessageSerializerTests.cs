using System;
using System.Collections.Generic;
using HotRepl.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlMessageSerializerTests
{
    [Fact]
    public void RoundTrip_ControlAuthMessage()
    {
        var msg = new ControlAuthMessage { Id = "auth-1", Token = "secret" };
        var back = RoundTrip<ControlAuthMessage>(msg);

        Assert.Equal(MessageType.ControlAuth, back.Type);
        Assert.Equal("auth-1", back.Id);
        Assert.Equal("secret", back.Token);
    }

    [Fact]
    public void RoundTrip_ControlAuthResultMessage()
    {
        var msg = new ControlAuthResultMessage
        {
            Id = "auth-1",
            Ok = true,
            SessionId = "session-1",
        };
        var back = RoundTrip<ControlAuthResultMessage>(msg);

        Assert.Equal(MessageType.ControlAuthResult, back.Type);
        Assert.Equal("auth-1", back.Id);
        Assert.True(back.Ok);
        Assert.Equal("session-1", back.SessionId);
    }

    [Fact]
    public void RoundTrip_LeaseAcquireMessage()
    {
        var msg = new LeaseAcquireMessage
        {
            Id = "lease-1",
            SessionId = "session-1",
            ClientName = "ardenfall-export",
        };
        var back = RoundTrip<LeaseAcquireMessage>(msg);

        Assert.Equal(MessageType.LeaseAcquire, back.Type);
        Assert.Equal("lease-1", back.Id);
        Assert.Equal("session-1", back.SessionId);
        Assert.Equal("ardenfall-export", back.ClientName);
    }

    [Fact]
    public void RoundTrip_LeaseAcquireResultMessage()
    {
        var msg = new LeaseAcquireResultMessage
        {
            Id = "lease-1",
            Ok = true,
            LeaseId = "lease-token",
        };
        var back = RoundTrip<LeaseAcquireResultMessage>(msg);

        Assert.Equal(MessageType.LeaseAcquireResult, back.Type);
        Assert.Equal("lease-1", back.Id);
        Assert.True(back.Ok);
        Assert.Equal("lease-token", back.LeaseId);
    }

    [Fact]
    public void RoundTrip_CommandDescribeMessage()
    {
        var msg = new CommandDescribeMessage { Id = "describe-1" };
        var back = RoundTrip<CommandDescribeMessage>(msg);

        Assert.Equal(MessageType.CommandDescribe, back.Type);
        Assert.Equal("describe-1", back.Id);
    }

    [Fact]
    public void RoundTrip_CommandDescribeResultMessage()
    {
        var descriptor = new CommandDescriptorMessage
        {
            Name = "archive.preflight",
            Version = 1,
            Kind = "sync",
            MutatesState = false,
            ArgsSchema = JObject.Parse("{\"type\":\"object\"}"),
            ResultSchema = JObject.Parse("{\"type\":\"object\"}"),
        };
        var msg = new CommandDescribeResultMessage
        {
            Id = "describe-1",
            Commands = new[] { descriptor },
        };
        var back = RoundTrip<CommandDescribeResultMessage>(msg);

        Assert.Equal(MessageType.CommandDescribeResult, back.Type);
        Assert.Equal("describe-1", back.Id);
        Assert.Single(back.Commands);
        Assert.Equal("archive.preflight", back.Commands[0].Name);
        Assert.Equal(1, back.Commands[0].Version);
        Assert.Equal("sync", back.Commands[0].Kind);
        Assert.False(back.Commands[0].MutatesState);
        Assert.Equal("object", back.Commands[0].ArgsSchema["type"]!.Value<string>());
    }

    [Fact]
    public void RoundTrip_CommandCallMessage()
    {
        var msg = new CommandCallMessage
        {
            Id = "cmd-1",
            LeaseId = "lease-token",
            Name = "archive.preflight",
            Args = JObject.Parse("{\"verbose\":true}"),
            TimeoutMs = 5000,
            IdempotencyKey = "run/preflight/1",
        };
        var back = RoundTrip<CommandCallMessage>(msg);

        Assert.Equal(MessageType.CommandCall, back.Type);
        Assert.Equal("cmd-1", back.Id);
        Assert.Null(back.LeaseId);
        Assert.Equal("archive.preflight", back.Name);
        Assert.True(back.Args["verbose"]!.Value<bool>());
        Assert.Equal(5000, back.TimeoutMs);
        Assert.Null(back.IdempotencyKey);
    }

    [Fact]
    public void RoundTrip_CommandResultMessage()
    {
        var msg = new CommandResultMessage
        {
            Id = "cmd-1",
            Status = "ok",
            Output = JObject.Parse("{\"passed\":true}"),
            Artifacts = new Dictionary<string, ArtifactRefMessage>(StringComparer.Ordinal)
            {
                ["items"] = ExampleArtifact(),
            },
        };
        var back = RoundTrip<CommandResultMessage>(msg);

        Assert.Equal(MessageType.CommandResult, back.Type);
        Assert.Equal("cmd-1", back.Id);
        Assert.Equal("ok", back.Status);
        Assert.True(back.Output["passed"]!.Value<bool>());
        var artifact = Assert.Single(back.Artifacts);
        Assert.Equal("items", artifact.Key);
        Assert.Equal("items.json", artifact.Value.LogicalName);
    }

    [Fact]
    public void RoundTrip_CommandErrorMessage()
    {
        var msg = new CommandErrorMessage
        {
            Id = "cmd-1",
            Status = "failed",
            Error = ExampleControlError("precondition_failed"),
            Diagnostics = new[] { ExampleControlError("diagnostic") },
        };
        var back = RoundTrip<CommandErrorMessage>(msg);

        Assert.Equal(MessageType.CommandError, back.Type);
        Assert.Equal("cmd-1", back.Id);
        Assert.Equal("failed", back.Status);
        Assert.Equal("precondition_failed", back.Error.Kind);
        Assert.True(back.Error.Retryable);
        Assert.Single(back.Diagnostics);
    }

    [Fact]
    public void RoundTrip_CommandAcceptedMessage()
    {
        var msg = new CommandAcceptedMessage
        {
            Id = "cmd-2",
            JobId = "job-1",
            State = "running",
        };
        var back = RoundTrip<CommandAcceptedMessage>(msg);

        Assert.Equal(MessageType.CommandAccepted, back.Type);
        Assert.Equal("cmd-2", back.Id);
        Assert.Equal("job-1", back.JobId);
        Assert.Equal("running", back.State);
    }

    [Fact]
    public void RoundTrip_JobStatusMessage()
    {
        var msg = new JobStatusMessage
        {
            Id = "status-1",
            LeaseId = "lease-token",
            JobId = "job-1",
        };
        var back = RoundTrip<JobStatusMessage>(msg);

        Assert.Equal(MessageType.JobStatus, back.Type);
        Assert.Equal("status-1", back.Id);
        Assert.Null(back.LeaseId);
        Assert.Equal("job-1", back.JobId);
    }

    [Fact]
    public void RoundTrip_JobStatusResultMessage()
    {
        var msg = new JobStatusResultMessage
        {
            Id = "status-1",
            JobId = "job-1",
            State = "running",
            Progress = JObject.Parse("{\"current\":10,\"total\":100}"),
        };
        var back = RoundTrip<JobStatusResultMessage>(msg);

        Assert.Equal(MessageType.JobStatusResult, back.Type);
        Assert.Equal("status-1", back.Id);
        Assert.Equal("job-1", back.JobId);
        Assert.Equal("running", back.State);
        Assert.Equal(10, back.Progress!["current"]!.Value<int>());
    }

    [Fact]
    public void RoundTrip_JobResultMessage()
    {
        var msg = new JobResultMessage
        {
            Id = "result-1",
            JobId = "job-1",
            State = "done",
            Status = "ok",
            Output = JObject.Parse("{\"done\":true}"),
            Artifacts = new Dictionary<string, ArtifactRefMessage>(StringComparer.Ordinal)
            {
                ["items"] = ExampleArtifact(),
            },
        };
        var back = RoundTrip<JobResultMessage>(msg);

        Assert.Equal(MessageType.JobResult, back.Type);
        Assert.Equal("result-1", back.Id);
        Assert.Equal("job-1", back.JobId);
        Assert.Equal("done", back.State);
        Assert.Equal("ok", back.Status);
        Assert.True(back.Output["done"]!.Value<bool>());
        Assert.Single(back.Artifacts);
    }

    [Fact]
    public void RoundTrip_JobCancelMessage()
    {
        var msg = new JobCancelMessage
        {
            Id = "cancel-1",
            LeaseId = "lease-token",
            JobId = "job-1",
        };
        var back = RoundTrip<JobCancelMessage>(msg);

        Assert.Equal(MessageType.JobCancel, back.Type);
        Assert.Equal("cancel-1", back.Id);
        Assert.Null(back.LeaseId);
        Assert.Equal("job-1", back.JobId);
    }

    [Fact]
    public void RoundTrip_JobCancelResultMessage()
    {
        var msg = new JobCancelResultMessage
        {
            Id = "cancel-1",
            Accepted = true,
            State = "running",
        };
        var back = RoundTrip<JobCancelResultMessage>(msg);

        Assert.Equal(MessageType.JobCancelResult, back.Type);
        Assert.Equal("cancel-1", back.Id);
        Assert.True(back.Accepted);
        Assert.Equal("running", back.State);
    }

    [Fact]
    public void RoundTrip_JobEventMessage()
    {
        var msg = new JobEventMessage
        {
            JobId = "job-1",
            Sequence = 4,
            State = "running",
            Progress = JObject.Parse("{\"phase\":\"exporting\"}"),
            Message = "Exported item batch 1",
        };
        var back = RoundTrip<JobEventMessage>(msg);

        Assert.Equal(MessageType.JobEvent, back.Type);
        Assert.Equal("job-1", back.JobId);
        Assert.Equal(4, back.Sequence);
        Assert.Equal("running", back.State);
        Assert.Equal("exporting", back.Progress!["phase"]!.Value<string>());
        Assert.Equal("Exported item batch 1", back.Message);
    }

    private static T RoundTrip<T>(T message) =>
        MessageSerializer.Deserialize<T>(MessageSerializer.Serialize(message));

    private static ArtifactRefMessage ExampleArtifact() =>
        new()
        {
            LogicalName = "items.json",
            Uri = "file:///tmp/items.json",
            Path = "/tmp/items.json",
            ContentType = "application/json",
            ByteSize = 128,
            Sha256 = new string('a', 64),
            Finalized = true,
        };

    private static ControlErrorMessage ExampleControlError(string kind) =>
        new()
        {
            Kind = kind,
            Code = "sampleCode",
            Message = "sample message",
            Retryable = true,
            Details = JObject.Parse("{\"field\":\"value\"}"),
        };
}
