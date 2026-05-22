using System;
using System.Collections.Generic;
using HotRepl.Protocol;
using HotRepl.Protocol.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ProtocolV2CleanupTests
{
    [Fact]
    public void CommandResult_SerializesOutputAndNamedArtifactsWithoutV1Fields()
    {
        var message = new CommandResultMessage
        {
            Id = "cmd-1",
            Status = "ok",
            Output = JObject.Parse("{\"done\":true}"),
            Artifacts = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal)
            {
                ["manifest"] = ExampleArtifact(),
            },
        };

        var json = ProtocolMessageSerializer.Serialize(message);

        Assert.Contains("\"output\"", json, StringComparison.Ordinal);
        Assert.Contains("\"manifest\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"result\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"diagnostics\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EvalError_UsesUniversalErrorEnvelope()
    {
        var message = new EvalErrorMessage
        {
            Id = "eval-1",
            Error = new HotReplErrorEnvelope(
                ErrorKind.Timeout,
                "evalTimeout",
                "Evaluation timed out.",
                retryable: false,
                details: new JObject()
            ),
        };

        var json = ProtocolMessageSerializer.Serialize(message);

        Assert.Contains("\"error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"timeout\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("errorKind", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JobControlMessages_DoNotSerializeLeaseFields()
    {
        var statusJson = ProtocolMessageSerializer.Serialize(
            new JobStatusMessage { Id = "status-1", JobId = "job-1" }
        );
        var cancelJson = ProtocolMessageSerializer.Serialize(
            new JobCancelMessage { Id = "cancel-1", JobId = "job-1" }
        );

        Assert.DoesNotContain("leaseId", statusJson, StringComparison.Ordinal);
        Assert.DoesNotContain("leaseId", cancelJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandAccepted_UsesJobAcceptedWireType()
    {
        var json = ProtocolMessageSerializer.Serialize(
            new JobAcceptedMessage
            {
                Id = "cmd-1",
                JobId = "job-1",
                State = "running",
            }
        );

        Assert.Contains("\"type\":\"job_accepted\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("command_accepted", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeMessageTypes_ComeFromPublicProtocolAssembly()
    {
        Assert.Equal("HotRepl.Protocol", typeof(CommandCallMessage).Assembly.GetName().Name);
        Assert.Equal("HotRepl.Protocol", typeof(CommandResultMessage).Assembly.GetName().Name);
        Assert.Equal("HotRepl.Protocol", typeof(EvalErrorMessage).Assembly.GetName().Name);
        Assert.Equal("HotRepl.Protocol", typeof(JobResultMessage).Assembly.GetName().Name);
        Assert.Equal("HotRepl.Protocol", typeof(ResetResultMessage).Assembly.GetName().Name);
        Assert.Equal("HotRepl.Protocol", typeof(SubscribeErrorMessage).Assembly.GetName().Name);
    }

    [Fact]
    public void PublicProtocolDtos_AreRecordTypes()
    {
        Assert.Equal(new ResetMessage { Id = "reset-1" }, new ResetMessage { Id = "reset-1" });
        Assert.Equal(
            new CommandSummary
            {
                Name = "game.quit",
                MajorVersion = 1,
                Kind = "sync",
                MutatesState = true,
            },
            new CommandSummary
            {
                Name = "game.quit",
                MajorVersion = 1,
                Kind = "sync",
                MutatesState = true,
            }
        );
    }

    [Fact]
    public void ReplConfig_DoesNotExposeV1AuthLeaseOptions()
    {
        Assert.Null(typeof(ReplConfig).GetProperty("RequireControlAuth"));
        Assert.Null(typeof(ReplConfig).GetProperty("ControlAuthToken"));
        Assert.Null(typeof(ReplConfig).GetProperty("RequireControlLease"));
        Assert.Null(typeof(ReplConfig).GetProperty("MaxControlMessageBytes"));
        Assert.Null(typeof(ReplConfig).GetProperty("MaxQueuedControlCommands"));
        Assert.Null(typeof(ReplConfig).GetProperty("ControlPlaneEnabled"));
        Assert.Null(typeof(ReplConfig).GetProperty("SchemaValidation"));
    }

    [Fact]
    public void ProtocolDefaults_DoNotAdvertiseRuntimeSchemaValidation()
    {
        Assert.False(new HandshakeMessage().Control.SchemaValidation);
    }

    private static ArtifactRef ExampleArtifact() =>
        new()
        {
            Uri = "hotrepl://artifact/manifest",
            Path = "/tmp/manifest.json",
            ContentType = "application/json",
            ByteSize = 12,
            Sha256 = "sha",
            Finalized = true,
        };
}
