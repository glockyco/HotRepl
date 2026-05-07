using System;
using System.Collections.Generic;
using System.Linq;

namespace HotRepl.Control;

/// <summary>Process-wide registry used by loaded host/game plugins to expose control commands.</summary>
public sealed class GlobalControlCommandRegistry : IControlCommandRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, IControlCommandHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>Shared registry used by host adapters.</summary>
    public static GlobalControlCommandRegistry Instance { get; } = new();

    /// <summary>Registers a handler until the returned registration is disposed.</summary>
    public IDisposable Register(IControlCommandHandler handler)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        var name = handler.Descriptor.Name;
        lock (_sync)
        {
            if (_handlers.ContainsKey(name))
                throw new InvalidOperationException($"Control command '{name}' is already registered.");
            _handlers.Add(name, handler);
        }

        return new Registration(this, name, handler);
    }

    /// <inheritdoc />
    public IReadOnlyList<ControlCommandDescriptor> Describe()
    {
        lock (_sync)
            return _handlers.Values.Select(h => h.Descriptor).OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public bool TryGet(string name, out IControlCommandHandler handler)
    {
        lock (_sync)
            return _handlers.TryGetValue(name, out handler!);
    }

    private void Unregister(string name, IControlCommandHandler handler)
    {
        lock (_sync)
        {
            if (_handlers.TryGetValue(name, out var current) && ReferenceEquals(current, handler))
                _handlers.Remove(name);
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly GlobalControlCommandRegistry _owner;
        private readonly string _name;
        private readonly IControlCommandHandler _handler;
        private bool _disposed;

        public Registration(GlobalControlCommandRegistry owner, string name, IControlCommandHandler handler)
        {
            _owner = owner;
            _name = name;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _owner.Unregister(_name, _handler);
        }
    }
}
