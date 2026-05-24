using System;
using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

/// <summary>JSON-friendly snapshot of a Unity GameObject.</summary>
public sealed class UnityGameObject
{
    /// <summary>Name of the matched GameObject.</summary>
    [Description("Name of the matched GameObject.")]
    public string Name { get; set; } = "";

    /// <summary>Whether the object is active in the hierarchy.</summary>
    [Description("activeInHierarchy state.")]
    public bool ActiveInHierarchy { get; set; }

    /// <summary>Unity layer index.</summary>
    [Description("Layer index.")]
    public int Layer { get; set; }

    /// <summary>Unity tag string.</summary>
    [Description("Tag string.")]
    public string Tag { get; set; } = "";

    /// <summary>World-space position.</summary>
    [Description("World-space position.")]
    public Vec3 Position { get; set; } = new();

    /// <summary>Type names of attached components in component order.</summary>
    [Description("Type names of attached components, in component order.")]
    public string[] ComponentTypeNames { get; set; } = Array.Empty<string>();
}
