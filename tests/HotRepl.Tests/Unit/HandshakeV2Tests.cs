using System;
using HotRepl.Evaluator;
using HotRepl.Server;
using Xunit;

namespace HotRepl.Tests.Unit;

public class HandshakeV2Tests
{
    [Fact]
    public void Handshake_AdvertisesProtocolVersionTwoAndNoAuthOrLease()
    {
        var json = RuntimeHandshakeFactory.Serialize(
            new ReplConfig(),
            new HostInfo
            {
                Name = "Tests",
                Version = "1.0.0",
                Runtime = ".NET",
                Platform = "Unity Test",
            },
            new EvaluatorCapabilities
            {
                Name = "Roslyn.Script",
                LanguageVersion = "latest",
                SupportsPersistentState = true,
                SupportsCompletion = false,
                TimeoutMode = TimeoutMode.Cooperative,
            },
            new[] { "Roslyn.Script" },
            new[] { "System" },
            new[] { "String[] Help()" }
        );

        Assert.Contains("\"protocolVersion\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"commandsListChanged\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"schemaValidation\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"maxMessageBytes\":4194304", json, StringComparison.Ordinal);
        Assert.Contains("\"maxJobConcurrency\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("controlPlane", json, StringComparison.Ordinal);
        Assert.DoesNotContain("authRequired", json, StringComparison.Ordinal);
        Assert.DoesNotContain("leaseRequired", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("leaseId", json, StringComparison.Ordinal);
    }
}
