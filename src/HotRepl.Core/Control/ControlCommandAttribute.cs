using System;

namespace HotRepl.Control;

/// <summary>
/// Declares command metadata on a handler type. When present, takes
/// precedence over the metadata properties on the handler instance.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ControlCommandAttribute : Attribute
{
    /// <summary>Create metadata for a typed control command.</summary>
    public ControlCommandAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Command name is required.", nameof(name));
        }

        Name = name;
    }

    /// <summary>Stable wire command name.</summary>
    public string Name { get; }

    /// <summary>Wire-protocol major version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Synchronous or job execution mode.</summary>
    public ControlCommandKind Kind { get; set; } = ControlCommandKind.Sync;

    /// <summary>True when this command may change game/runtime state.</summary>
    public bool MutatesState { get; set; }
}
