using System;

namespace HotRepl.Control;

/// <summary>
/// Declares an artifact key or key pattern a command handler may produce.
/// Multiple declarations are allowed on one handler type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ControlCommandArtifactAttribute : Attribute
{
    /// <summary>Create an artifact declaration for a logical key or key pattern.</summary>
    public ControlCommandArtifactAttribute(string keyPattern)
    {
        if (string.IsNullOrWhiteSpace(keyPattern))
        {
            throw new ArgumentException("Artifact key pattern is required.", nameof(keyPattern));
        }

        KeyPattern = keyPattern;
    }

    /// <summary>Logical artifact key or simple pattern such as <c>data.&lt;stem&gt;</c>.</summary>
    public string KeyPattern { get; }

    /// <summary>Expected artifact content type.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>True when the handler is expected to produce this artifact on success.</summary>
    public bool Required { get; set; }

    /// <summary>Cardinality hint, such as <c>1</c>, <c>0..1</c>, <c>1..*</c>, or <c>0..*</c>.</summary>
    public string RepeatCount { get; set; } = "1";
}
