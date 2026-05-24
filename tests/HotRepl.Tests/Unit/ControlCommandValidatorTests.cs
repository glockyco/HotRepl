using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Schema;
using Newtonsoft.Json.Linq;
using NJsonSchema;
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
    private static readonly JsonSchema ArgsSchema = SchemaCache.CompiledFor<Args>();

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
        var schema = SchemaCache.CompiledFor<EmptyArgs>();
        var result = Validator.Validate(JObject.Parse("{}"), schema);
        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Validate_AcceptsCompiledSchemaWithoutReparsingFromJObject()
    {
        var compiled = await JsonSchema.FromJsonAsync(
            "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}"
        );

        var ok = Validator.Validate(new JObject(new JProperty("name", "x")), compiled);
        var bad = Validator.Validate(new JObject(), compiled);

        Assert.True(ok.Ok);
        Assert.False(bad.Ok);
    }
}
