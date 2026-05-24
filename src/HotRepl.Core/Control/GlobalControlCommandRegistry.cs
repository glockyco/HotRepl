using System;
using System.Collections.Generic;
using System.Linq;
using HotRepl.Control.Internal;
using HotRepl.Control.Schema;
using Newtonsoft.Json;

namespace HotRepl.Control;

/// <summary>
/// Process-wide registry used by loaded host/game plugins to expose
/// control commands. Typed handlers passed to <c>Register</c> are
/// wrapped in a <see cref="TypedCommandAdapter{TArgs,TOutput}"/>.
/// </summary>
public sealed class GlobalControlCommandRegistry : IControlCommandRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ICompiledControlCommand> _handlers = new(
        StringComparer.Ordinal
    );
    private readonly JsonSerializer _serializer;
    private readonly IControlCommandValidator _validator;

    /// <summary>Shared registry used by host adapters.</summary>
    public static GlobalControlCommandRegistry Instance { get; } = new();

    /// <summary>Public ctor for unit tests; production code uses <see cref="Instance"/>.</summary>
    public GlobalControlCommandRegistry()
        : this(JsonSerializer.CreateDefault(), new NJsonSchemaValidator()) { }

    internal GlobalControlCommandRegistry(
        JsonSerializer serializer,
        IControlCommandValidator validator
    )
    {
        _serializer = serializer;
        _validator = validator;
    }

    /// <inheritdoc />
    public IDisposable Register<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var compiled = new TypedCommandAdapter<TArgs, TOutput>(handler, _serializer, _validator);
        var name = compiled.Descriptor.Name;

        lock (_sync)
        {
            if (_handlers.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"Control command '{name}' is already registered."
                );
            }

            _handlers.Add(name, compiled);
        }

        return new Registration(this, name, compiled);
    }

    /// <inheritdoc />
    public IReadOnlyList<ControlCommandDescriptor> Describe()
    {
        lock (_sync)
        {
            return _handlers
                .Values.Select(h => h.Descriptor)
                .OrderBy(d => d.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public bool TryGet(string name, out ICompiledControlCommand? handler)
    {
        lock (_sync)
        {
            return _handlers.TryGetValue(name, out handler!);
        }
    }

    private void Unregister(string name, ICompiledControlCommand handler)
    {
        lock (_sync)
        {
            if (_handlers.TryGetValue(name, out var current) && ReferenceEquals(current, handler))
            {
                _handlers.Remove(name);
            }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly GlobalControlCommandRegistry _owner;
        private readonly string _name;
        private readonly ICompiledControlCommand _handler;
        private bool _disposed;

        public Registration(
            GlobalControlCommandRegistry owner,
            string name,
            ICompiledControlCommand handler
        )
        {
            _owner = owner;
            _name = name;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Unregister(_name, _handler);
        }
    }
}
