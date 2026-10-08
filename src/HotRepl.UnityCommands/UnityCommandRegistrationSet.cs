using System;
using System.Collections.Generic;

namespace HotRepl.UnityCommands;

internal sealed class UnityCommandRegistrationSet : IDisposable
{
    private readonly List<IDisposable> _registrations = new();

    internal static UnityCommandRegistrationSet Register(
        IReadOnlyList<string> names,
        Func<int, IDisposable> register,
        bool enabled,
        string disabledCsv,
        Action<string> log
    )
    {
        var registrations = new UnityCommandRegistrationSet();
        if (!enabled)
        {
            log("HotRepl.UnityCommands disabled via config; skipping registration.");
            return registrations;
        }

        var disabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in disabledCsv.Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0)
                disabled.Add(name);
        }

        try
        {
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                if (disabled.Contains(name))
                {
                    log($"Skipping disabled command: {name}");
                    continue;
                }

                registrations._registrations.Add(register(i));
                log($"Registered command: {name}");
            }
            return registrations;
        }
        catch
        {
            registrations.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
            registration.Dispose();
        _registrations.Clear();
    }
}
