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
    public void Write_CreatesV2DocumentWithoutAuthOrLeaseFields()
    {
        var config = new ReplConfig { BindHost = "127.0.0.1", Port = 18590 };
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
        Assert.True(json["controlPlane"]!["supported"]!.Value<bool>());
        Assert.Equal(2, json["controlPlane"]!["protocolVersion"]!.Value<int>());
        Assert.Null(json["controlPlane"]!["authRequired"]);
        Assert.Null(json["controlPlane"]!["leaseRequired"]);
        Assert.Null(json["auth"]);
        Assert.DoesNotContain("auth", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lease", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_RemovesDocument()
    {
        var config = new ReplConfig();
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
