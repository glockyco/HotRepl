using System;
using System.IO;
using HotRepl.Discovery;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class InstanceDocumentWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_CreatesDocumentWithoutTokenValue()
    {
        var config = new ReplConfig
        {
            BindHost = "127.0.0.1",
            Port = 18590,
            ControlAuthToken = "super-secret-token",
            RequireControlAuth = true,
            RequireControlLease = true,
        };
        var host = new HostInfo
        {
            Name = "BepInEx",
            Version = "1.2.3",
            Runtime = "Mono",
            Platform = "Unity",
        };
        using var writer = InstanceDocumentWriter.Write(
            config,
            host,
            _root,
            instanceId: "test-instance"
        );

        var raw = File.ReadAllText(writer.Path);
        var json = JObject.Parse(raw);

        Assert.Equal(1, json["schemaVersion"]!.Value<int>());
        Assert.Equal("test-instance", json["instanceId"]!.Value<string>());
        Assert.Equal("ws://127.0.0.1:18590", json["url"]!.Value<string>());
        Assert.Equal("BepInEx", json["host"]!["name"]!.Value<string>());
        Assert.True(json["controlPlane"]!["authRequired"]!.Value<bool>());
        Assert.True(json["controlPlane"]!["leaseRequired"]!.Value<bool>());
        Assert.StartsWith(
            "sha256:",
            json["auth"]!["fingerprint"]!.Value<string>(),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("super-secret-token", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_RemovesDocument()
    {
        var config = new ReplConfig { ControlAuthToken = "secret", RequireControlAuth = true };
        var host = new HostInfo
        {
            Name = "BepInEx",
            Version = "1.2.3",
            Runtime = "Mono",
            Platform = "Unity",
        };
        var writer = InstanceDocumentWriter.Write(config, host, _root, instanceId: "test-instance");
        var path = writer.Path;

        writer.Dispose();

        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
