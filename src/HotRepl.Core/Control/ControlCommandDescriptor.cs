using System;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Machine-readable metadata for a registered control-plane command.</summary>
public sealed record ControlCommandDescriptor
{
    public ControlCommandDescriptor(
        string name,
        int version,
        ControlCommandKind kind,
        bool mutatesState,
        JObject argsSchema,
        JObject resultSchema,
        JObject? artifactsSchema = null
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name is required.", nameof(name));
        if (version <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Command version must be positive."
            );

        Name = name;
        Version = version;
        Kind = kind;
        MutatesState = mutatesState;
        ArgsSchema = argsSchema ?? throw new ArgumentNullException(nameof(argsSchema));
        ResultSchema = resultSchema ?? throw new ArgumentNullException(nameof(resultSchema));
        ArtifactsSchema = artifactsSchema ?? EmptyObjectSchema();
    }

    public string Name { get; }
    public int Version { get; }
    public ControlCommandKind Kind { get; }
    public bool MutatesState { get; }
    public JObject ArgsSchema { get; }
    public JObject ResultSchema { get; }
    public JObject ArtifactsSchema { get; }

    private static JObject EmptyObjectSchema() => JObject.Parse("{\"type\":\"object\"}");
}
