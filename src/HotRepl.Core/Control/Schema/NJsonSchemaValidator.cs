using System.Linq;
using Newtonsoft.Json.Linq;
using NJsonSchema;

namespace HotRepl.Control.Schema;

/// <summary>
/// Default <see cref="IControlCommandValidator"/>. Internalized
/// NJsonSchema parses the schema and validates the args. Validation
/// errors are projected to strings for the wire-shape diagnostic.
/// </summary>
internal sealed class NJsonSchemaValidator : IControlCommandValidator
{
    /// <inheritdoc />
    public SchemaValidationResult Validate(JObject args, JsonSchema schema)
    {
        var errors = schema.Validate(args);
        if (errors.Count == 0)
        {
            return SchemaValidationResult.Pass;
        }

        var formatted = errors
            .Select(e => string.IsNullOrEmpty(e.Path) ? e.Kind.ToString() : $"{e.Path}: {e.Kind}")
            .ToArray();
        return new SchemaValidationResult(false, formatted);
    }
}
