using System;
using System.Collections.Generic;
using HotRepl.Control.Internal;

namespace HotRepl.Control;

/// <summary>Default command registry used by hosts that expose no control-plane commands.</summary>
public sealed class EmptyControlCommandRegistry : IControlCommandRegistry
{
    /// <summary>Process-wide singleton.</summary>
    public static EmptyControlCommandRegistry Instance { get; } = new();

    private EmptyControlCommandRegistry() { }

    /// <inheritdoc />
    public IReadOnlyList<ControlCommandDescriptor> Describe() =>
        Array.Empty<ControlCommandDescriptor>();

    /// <inheritdoc />
    public IDisposable Register<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput> handler) =>
        NullRegistration.Instance;

    /// <inheritdoc />
    public bool TryGet(string name, out ICompiledControlCommand? handler)
    {
        handler = null;
        return false;
    }

    private sealed class NullRegistration : IDisposable
    {
        public static NullRegistration Instance { get; } = new();

        public void Dispose() { }
    }
}
