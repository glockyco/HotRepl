using System;
using System.Collections.Generic;

namespace HotRepl.Control;

/// <summary>Default command registry used by hosts that expose no control-plane commands.</summary>
public sealed class EmptyControlCommandRegistry : IControlCommandRegistry
{
    public static EmptyControlCommandRegistry Instance { get; } = new();

    private EmptyControlCommandRegistry() { }

    public IReadOnlyList<ControlCommandDescriptor> Describe() => Array.Empty<ControlCommandDescriptor>();

    public bool TryGet(string name, out IControlCommandHandler handler)
    {
        handler = null!;
        return false;
    }
}
