using System;
using BepInEx;
using HotRepl.Control;

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

    private IDisposable? _registrations;

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

        _registrations = UnityCommandCatalog.Register(
            GlobalControlCommandRegistry.Instance,
            routine =>
            {
                StartCoroutine(routine);
            },
            enabledEntry.Value,
            disabledEntry.Value,
            message => Logger.LogInfo(message)
        );
    }

    private void OnDestroy()
    {
        _registrations?.Dispose();
        _registrations = null;
    }
}
