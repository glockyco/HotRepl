using System;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using NJsonSchema;

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
    private static readonly ConcurrentDictionary<Type, JsonSchema> CompiledCache = new();

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

    /// <summary>Compiled schema for the given type, computed on first request and cached.</summary>
    public static JsonSchema CompiledFor<T>() => CompiledFor(typeof(T));

    /// <summary>Compiled schema for the given type, computed on first request and cached.</summary>
    public static JsonSchema CompiledFor(Type type) =>
        CompiledCache.GetOrAdd(type, BuildCompiledSchema);

    private static JObject BuildSchema(Type type)
    {
        return JObject.Parse(CompiledFor(type).ToJson());
    }

    private static JsonSchema BuildCompiledSchema(Type type)
    {
        if (type == typeof(EmptyArgs))
        {
            return JsonSchema
                .FromJsonAsync(EmptyObject.ToString(Newtonsoft.Json.Formatting.None))
                .GetAwaiter()
                .GetResult();
        }

        return JsonSchema.FromType(type, BuilderSettings);
    }
}
