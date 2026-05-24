using System;
using System.Collections.Generic;
using HotRepl.Control;
using HotRepl.UnityCommands.Screenshots;
using MelonLoader;

[assembly: MelonInfo(
    typeof(HotRepl.UnityCommands.MelonLoader.UnityCommandsMod),
    "HotRepl Unity Commands",
    "3.0.0",
    "glockyco"
)]

namespace HotRepl.UnityCommands.MelonLoader;

/// <summary>MelonLoader entry point that registers the first-party Unity command catalog.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "MelonLoader owns the mod lifecycle; OnDeinitializeMelon disposes command registrations."
)]
public sealed class UnityCommandsMod : MelonMod
{
    private readonly List<IDisposable> _registrations = new();
    private MelonPreferences_Entry<bool> _enabled = null!;
    private MelonPreferences_Entry<string> _disabled = null!;

    /// <inheritdoc />
    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory("HotRepl.UnityCommands");
        _enabled = category.CreateEntry(
            "Enabled",
            true,
            description: "Master switch. When false, no UnityCommands handlers are registered. Changes apply on next game start."
        );
        _disabled = category.CreateEntry(
            "Disabled",
            "",
            description: "Comma-separated command names to skip, e.g. 'unity.time.set_scale, unity.screenshot.capture'. Changes apply on next game start."
        );
    }

    /// <inheritdoc />
    public override void OnLateInitializeMelon()
    {
        if (!_enabled.Value)
        {
            LoggerInstance.Msg("HotRepl.UnityCommands disabled via config; skipping registration.");
            return;
        }

        RegisterEnabledCommands(ParseCsv(_disabled.Value));
    }

    /// <inheritdoc />
    public override void OnDeinitializeMelon()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    private void RegisterEnabledCommands(HashSet<string> disabled)
    {
        var factories = UnityCommandCatalog.Build(
            new EndOfFrameUnityScreenshotCapturer(routine =>
            {
                MelonCoroutines.Start(routine);
            })
        );
        var names = UnityCommandCatalog.Names;
        for (var i = 0; i < factories.Count; i++)
        {
            var name = names[i];
            if (disabled.Contains(name))
            {
                LoggerInstance.Msg($"Skipping disabled command: {name}");
                continue;
            }

            _registrations.Add(factories[i](GlobalControlCommandRegistry.Instance));
            LoggerInstance.Msg($"Registered command: {name}");
        }
    }

    private static HashSet<string> ParseCsv(string csv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv))
        {
            return set;
        }

        foreach (var raw in csv.Split(','))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                set.Add(trimmed);
            }
        }

        return set;
    }
}
