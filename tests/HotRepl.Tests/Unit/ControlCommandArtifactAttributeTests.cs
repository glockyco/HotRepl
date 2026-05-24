using System;
using System.Linq;
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

[ControlCommandArtifact(
    "data.<stem>",
    ContentType = "application/json",
    Required = true,
    RepeatCount = "1..*"
)]
[ControlCommandArtifact("screenshots.metadata", ContentType = "application/json")]
file sealed class ExampleArtifactHandler { }

public sealed class ControlCommandArtifactAttributeTests
{
    [Fact]
    public void Attributes_AreReadable()
    {
        var attrs = typeof(ExampleArtifactHandler)
            .GetCustomAttributes(typeof(ControlCommandArtifactAttribute), inherit: false)
            .Cast<ControlCommandArtifactAttribute>()
            .OrderBy(a => a.KeyPattern, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, attrs.Length);
        Assert.Equal("data.<stem>", attrs[0].KeyPattern);
        Assert.Equal("application/json", attrs[0].ContentType);
        Assert.Equal("1..*", attrs[0].RepeatCount);
        Assert.True(attrs[0].Required);
    }
}
