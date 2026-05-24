using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Schema;

/// <summary>Outcome of a schema validation.</summary>
public readonly struct SchemaValidationResult : IEquatable<SchemaValidationResult>
{
    /// <summary>Create a validation result.</summary>
    public SchemaValidationResult(bool ok, IReadOnlyList<string> errors)
    {
        Ok = ok;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>True when args satisfy the schema.</summary>
    public bool Ok { get; }

    /// <summary>One human-readable string per validation error; empty on pass.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Reusable success singleton.</summary>
    public static SchemaValidationResult Pass { get; } = new(true, Array.Empty<string>());

    /// <summary>
    /// Project this failure into a wire-shape <see cref="ControlCommandError"/>
    /// for the diagnostic envelope.
    /// </summary>
    public ControlCommandError ToDiagnostic()
    {
        var first = Errors.Count > 0 ? Errors[0] : "Argument schema validation failed.";
        var details = new JObject { ["errors"] = JArray.FromObject(Errors) };
        return new ControlCommandError(
            Kind: "validation_failed",
            Code: "argsSchemaViolation",
            Message: first,
            Retryable: false,
            Details: details
        );
    }

    /// <inheritdoc />
    public bool Equals(SchemaValidationResult other) => Ok == other.Ok && Errors == other.Errors;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SchemaValidationResult o && Equals(o);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Ok, Errors);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(SchemaValidationResult left, SchemaValidationResult right) =>
        left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(SchemaValidationResult left, SchemaValidationResult right) =>
        !left.Equals(right);
}
