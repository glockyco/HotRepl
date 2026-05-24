using Newtonsoft.Json.Linq;
using NJsonSchema;

namespace HotRepl.Control.Schema;

/// <summary>Validates command arguments against a JSON schema.</summary>
public interface IControlCommandValidator
{
    /// <summary>Apply <paramref name="schema"/> to <paramref name="args"/> and report the outcome.</summary>
    SchemaValidationResult Validate(JObject args, JsonSchema schema);
}
