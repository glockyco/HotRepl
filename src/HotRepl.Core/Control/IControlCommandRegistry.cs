using System.Collections.Generic;

namespace HotRepl.Control;

/// <summary>Registry of host-provided control-plane commands.</summary>
public interface IControlCommandRegistry
{
    /// <summary>Returns command descriptors advertised to clients.</summary>
    IReadOnlyList<ControlCommandDescriptor> Describe();

    /// <summary>Finds a command handler by command name.</summary>
    bool TryGet(string name, out IControlCommandHandler handler);
}
