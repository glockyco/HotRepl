using System;
using System.Collections.Generic;
using HotRepl.Protocol;
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
            Artifacts = new Dictionary<string, ArtifactRefMessage>(StringComparer.Ordinal)
            {
                ["manifest"] = ExampleArtifact(),
            },
        };

        var json = MessageSerializer.Serialize(message);

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
            Error = new ControlErrorMessage
            {
                Kind = "timeout",
                Code = "evalTimeout",
                Message = "Evaluation timed out.",
                Retryable = false,
                Details = new JObject(),
            },
        };

        var json = MessageSerializer.Serialize(message);

        Assert.Contains("\"error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"timeout\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("errorKind", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JobControlMessages_DoNotSerializeLeaseFields()
    {
        var statusJson = MessageSerializer.Serialize(
            new JobStatusMessage { Id = "status-1", JobId = "job-1" }
        );
        var cancelJson = MessageSerializer.Serialize(
            new JobCancelMessage { Id = "cancel-1", JobId = "job-1" }
        );

        Assert.DoesNotContain("leaseId", statusJson, StringComparison.Ordinal);
        Assert.DoesNotContain("leaseId", cancelJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandAccepted_UsesJobAcceptedWireType()
    {
        var json = MessageSerializer.Serialize(
            new CommandAcceptedMessage
            {
                Id = "cmd-1",
                JobId = "job-1",
                State = "running",
            }
        );

        Assert.Contains("\"type\":\"job_accepted\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("command_accepted", json, StringComparison.Ordinal);
    }

    private static ArtifactRefMessage ExampleArtifact() =>
        new()
        {
            LogicalName = "manifest",
            Uri = "hotrepl://artifact/manifest",
            Path = "/tmp/manifest.json",
            ContentType = "application/json",
            ByteSize = 12,
            Sha256 = "sha",
            Finalized = true,
        };
}
