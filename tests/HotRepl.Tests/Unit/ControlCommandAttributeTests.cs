using System.Linq;
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

[ControlCommand("test.example", Version = 2, Kind = ControlCommandKind.Job, MutatesState = true)]
file sealed class ExampleHandler { }

public sealed class ControlCommandAttributeTests
{
    [Fact]
    public void Attribute_ExposesAllMetadataFields()
    {
        var attr = typeof(ExampleHandler)
            .GetCustomAttributes(typeof(ControlCommandAttribute), inherit: false)
            .Cast<ControlCommandAttribute>()
            .Single();

        Assert.Equal("test.example", attr.Name);
        Assert.Equal(2, attr.Version);
        Assert.Equal(ControlCommandKind.Job, attr.Kind);
        Assert.True(attr.MutatesState);
    }
}
