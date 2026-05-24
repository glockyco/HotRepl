using System;
using System.Collections.Generic;

namespace HotRepl.Control;

/// <summary>Registry of host-provided control-plane commands.</summary>
public interface IControlCommandRegistry
{
    /// <summary>Descriptors advertised to clients.</summary>
    IReadOnlyList<ControlCommandDescriptor> Describe();

    /// <summary>
    /// Register a typed command. The returned disposable unregisters on
    /// dispose; use it for teardown in plugin <c>OnDestroy</c> /
    /// <c>OnDeinitializeMelon</c>.
    /// </summary>
    IDisposable Register<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput> handler);
}
