using System;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Schema;

/// <summary>
/// JSON schema cache keyed by .NET type. Backed by NJsonSchema 10.9.0
/// (ILRepack-internalized in HotRepl.Core.dll). Schemas are pre-warmed
/// at adapter construction so the first agent request never pays for
/// reflection.
/// </summary>
public static class SchemaCache
{
    private static readonly ConcurrentDictionary<Type, JObject> Cache = new();

    private static readonly NJsonSchema.Generation.JsonSchemaGeneratorSettings BuilderSettings =
        new()
        {
            DefaultReferenceTypeNullHandling = NJsonSchema
                .Generation
                .ReferenceTypeNullHandling
                .Null,
            AllowReferencesWithProperties = false,
        };

    /// <summary>
    /// Open-object schema with <c>additionalProperties: true</c>; the
    /// universal artifacts-schema fallback.
    /// </summary>
    public static JObject AnyObject { get; } =
        JObject.Parse("{ \"type\": \"object\", \"additionalProperties\": true }");

    /// <summary>
    /// Closed-object schema with <c>additionalProperties: false</c>;
    /// the <see cref="EmptyArgs"/> schema.
    /// </summary>
    public static JObject EmptyObject { get; } =
        JObject.Parse("{ \"type\": \"object\", \"additionalProperties\": false }");

    /// <summary>Schema for the given type, computed on first request and cached.</summary>
    public static JObject For<T>() => For(typeof(T));

    /// <summary>Schema for the given type, computed on first request and cached.</summary>
    public static JObject For(Type type) => Cache.GetOrAdd(type, BuildSchema);

    private static JObject BuildSchema(Type type)
    {
        if (type == typeof(EmptyArgs))
        {
            return EmptyObject;
        }

        var schema = NJsonSchema.JsonSchema.FromType(type, BuilderSettings);
        return JObject.Parse(schema.ToJson());
    }
}
