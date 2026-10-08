using System;
using HotRepl.Control;
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
    private IDisposable _registrations;
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
        _registrations = UnityCommandCatalog.Register(
            GlobalControlCommandRegistry.Instance,
            routine =>
            {
                MelonCoroutines.Start(routine);
            },
            _enabled.Value,
            _disabled.Value,
            message => LoggerInstance.Msg(message)
        );
    }

    /// <inheritdoc />
    public override void OnDeinitializeMelon()
    {
        _registrations?.Dispose();
        _registrations = null;
    }
}
