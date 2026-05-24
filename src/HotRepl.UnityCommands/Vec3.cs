using System.ComponentModel;

namespace HotRepl.UnityCommands;

/// <summary>
/// JSON-friendly vector POCO used in UnityCommands schemas instead of UnityEngine.Vector3.
/// </summary>
public sealed class Vec3
{
    /// <summary>X component.</summary>
    [Description("X component.")]
    public float X { get; set; }

    /// <summary>Y component.</summary>
    [Description("Y component.")]
    public float Y { get; set; }

    /// <summary>Z component.</summary>
    [Description("Z component.")]
    public float Z { get; set; }

    /// <summary>Converts a Unity vector to its JSON-friendly shape.</summary>
    public static Vec3 From(UnityEngine.Vector3 value) =>
        new()
        {
            X = value.x,
            Y = value.y,
            Z = value.z,
        };
}
