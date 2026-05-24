using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HotRepl.Control;
using HotRepl.Control.Schema;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using Xunit;

namespace HotRepl.Tests.Unit;

public class SchemaCacheTests
{
    private sealed class TestArgs
    {
        [Required]
        [Range(0, 100)]
        [Description("0-100 inclusive.")]
        public int Value { get; set; }

        public string? OptionalNote { get; set; }
    }

    [Fact]
    public void EmptyArgs_EmitsAdditionalPropertiesFalseSchema()
    {
        var schema = SchemaCache.For<EmptyArgs>();

        Assert.Equal("object", (string?)schema["type"]);
        Assert.False((bool)schema["additionalProperties"]!);
    }

    [Fact]
    public void TypedArgs_HonorsRequiredRangeAndDescription()
    {
        var schema = SchemaCache.For<TestArgs>();

        Assert.Equal("object", (string?)schema["type"]);
        Assert.NotNull(schema["properties"]);

        var value = schema["properties"]!["Value"]!;
        Assert.Equal("integer", (string?)value["type"]);
        Assert.Equal(0, (int)value["minimum"]!);
        Assert.Equal(100, (int)value["maximum"]!);
        Assert.Equal("0-100 inclusive.", (string?)value["description"]);

        var required = (IList<JToken>)schema["required"]!;
        Assert.Contains("Value", required.Select(t => (string?)t));
    }

    [Fact]
    public void For_CachesSchemaPerType()
    {
        var a = SchemaCache.For<TestArgs>();
        var b = SchemaCache.For<TestArgs>();
        Assert.Same(a, b);
    }

    [Fact]
    public void CompiledFor_CachesSchemaPerType()
    {
        var a = SchemaCache.CompiledFor<TestArgs>();
        var b = SchemaCache.CompiledFor<TestArgs>();

        Assert.Same(a, b);
        Assert.IsType<JsonSchema>(a);
    }

    [Fact]
    public void CompiledFor_AndFor_AreSemanticallyEquivalent()
    {
        var compiled = SchemaCache.CompiledFor<TestArgs>();
        var json = SchemaCache.For<TestArgs>();

        Assert.Equal(
            json.ToString(Newtonsoft.Json.Formatting.None),
            compiled.ToJson(Newtonsoft.Json.Formatting.None)
        );
    }

    [Fact]
    public void AnyObject_IsConstant()
    {
        Assert.Equal("object", (string?)SchemaCache.AnyObject["type"]);
        Assert.True((bool)SchemaCache.AnyObject["additionalProperties"]!);
    }
}
