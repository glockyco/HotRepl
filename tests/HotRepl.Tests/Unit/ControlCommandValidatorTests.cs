using System.ComponentModel.DataAnnotations;
using HotRepl.Control;
using HotRepl.Control.Schema;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlCommandValidatorTests
{
    private sealed class Args
    {
        [Required]
        [Range(0, 100)]
        public int Value { get; set; }
    }

    private static readonly NJsonSchemaValidator Validator = new();
    private static readonly JObject ArgsSchema = SchemaCache.For<Args>();

    [Fact]
    public void Validate_PassesValidArgs()
    {
        var result = Validator.Validate(JObject.Parse("{\"Value\": 42}"), ArgsSchema);
        Assert.True(result.Ok);
    }

    [Fact]
    public void Validate_FailsOnRangeViolation()
    {
        var result = Validator.Validate(JObject.Parse("{\"Value\": 200}"), ArgsSchema);
        Assert.False(result.Ok);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_FailsOnMissingRequired()
    {
        var result = Validator.Validate(JObject.Parse("{}"), ArgsSchema);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Validate_PassesEmptyArgsAgainstClosedSchema()
    {
        var schema = SchemaCache.For<EmptyArgs>();
        var result = Validator.Validate(JObject.Parse("{}"), schema);
        Assert.True(result.Ok);
    }
}
