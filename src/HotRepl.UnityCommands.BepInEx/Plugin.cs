using System;
using System.Collections.Generic;
using BepInEx;
using HotRepl.Control;
using HotRepl.UnityCommands.Screenshots;

namespace HotRepl.UnityCommands.BepInEx;

/// <summary>BepInEx entry point that registers the first-party Unity command catalog.</summary>
[BepInPlugin(PluginGuid, "HotRepl Unity Commands", Version)]
[BepInDependency("hotrepl.bepinex", BepInDependency.DependencyFlags.HardDependency)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "BepInEx owns the plugin lifecycle; OnDestroy disposes command registrations."
)]
public sealed class Plugin : BaseUnityPlugin
{
    /// <summary>BepInEx plugin GUID.</summary>
    public const string PluginGuid = "hotrepl.unitycommands.bepinex";

    private const string Version = "3.0.0";

    private readonly List<IDisposable> _registrations = new();

    private void Awake()
    {
        var enabledEntry = Config.Bind(
            "General",
            "Enabled",
            true,
            "Master switch. When false, no UnityCommands handlers are registered. Changes apply on next game start."
        );
        var disabledEntry = Config.Bind(
            "Commands",
            "Disabled",
            "",
            "Comma-separated command names to skip, e.g. 'unity.time.set_scale, unity.screenshot.capture'. Changes apply on next game start."
        );

        if (!enabledEntry.Value)
        {
            Logger.LogInfo("HotRepl.UnityCommands disabled via config; skipping registration.");
            return;
        }

        RegisterEnabledCommands(ParseCsv(disabledEntry.Value));
    }

    private void OnDestroy()
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
                StartCoroutine(routine);
            })
        );
        var names = UnityCommandCatalog.Names;
        for (var i = 0; i < factories.Count; i++)
        {
            var commandName = names[i];
            if (disabled.Contains(commandName))
            {
                Logger.LogInfo($"Skipping disabled command: {commandName}");
                continue;
            }

            _registrations.Add(factories[i](GlobalControlCommandRegistry.Instance));
            Logger.LogInfo($"Registered command: {commandName}");
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
