using System;
using System.Threading;
using HotRepl.Control;
using HotRepl.Helpers.Unity;
using HotRepl.UnityCommands;
using Lunaris;
using UnityEngine;

namespace HotRepl.Host.Lunaris;

/// <summary>Lunaris entry point for the REPL and first-party Unity commands.</summary>
[LunarisPlugin("HotRepl", "3.0.0", "glockyco", "Runtime C# REPL and Unity command bridge.")]
[LunarisPermission(
    LunarisPermission.Network
        | LunarisPermission.FileAccess
        | LunarisPermission.Reflection
        | LunarisPermission.Unsafe
)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Lunaris owns the MonoBehaviour lifecycle; OnDestroy disposes the engine and command registrations."
)]
public sealed class ReplPlugin : LunarisPlugin
{
    private ReplEngine? _engine;
    private IDisposable? _registrations;

    private void Awake()
    {
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var host = new LunarisHost(Logging, LoadConfig());
            UnityHelpers.Initialize(this);
            _registrations = UnityCommandCatalog.Register(
                GlobalControlCommandRegistry.Instance,
                routine =>
                {
                    StartCoroutine(routine);
                },
                ReadConfig(
                    "General",
                    "Enabled",
                    true,
                    "Master switch. When false, no UnityCommands handlers are registered. Changes apply on next plugin enable or reload."
                ),
                ReadConfig(
                    "Commands",
                    "Disabled",
                    "",
                    "Comma-separated command names to skip, e.g. 'unity.time.set_scale, unity.screenshot.capture'. Changes apply on next plugin enable or reload."
                ),
                message => Logging.LogInfo(message)
            );
            _engine = new ReplEngine(host);
            _engine.Start();
            Logging.LogInfo($"HotRepl loaded — REPL on {host.Config.BindHost}:{host.Config.Port}.");
        }
        catch (Exception ex)
        {
            Logging.LogError($"HotRepl failed to start: {ex}");
            Cleanup();
        }
    }

    private ReplConfig LoadConfig()
    {
        var config = new ReplConfig
        {
            Port = ReadConfig("Server", "Port", 18590, "WebSocket listen port."),
            BindHost = ReadConfig(
                "Server",
                "BindHost",
                "127.0.0.1",
                "WebSocket bind host. Use 127.0.0.1 for loopback-only, or explicitly set 0.0.0.0 for host-reachable automation."
            ),
        };
        var artifactDirectory = ReadConfig(
            "Server",
            "ArtifactDirectory",
            "",
            "Directory for command artifacts. Empty uses the per-user state directory. A game under Wine or Proton writes a Windows path, so point this at a mapped drive when the client reads artifacts from the host filesystem."
        );
        if (!string.IsNullOrWhiteSpace(artifactDirectory))
            config.ArtifactDirectory = artifactDirectory.Trim();
        config.ApplyEnvironmentOverrides();
        foreach (var warning in ReplConfigExposurePolicy.Validate(config).Warnings)
            Logging.LogWarning(warning);
        return config;
    }

    private T ReadConfig<T>(string section, string name, T defaultValue, string description)
    {
        var key = $"{section}.{name}";
        var value = Config.Read(key, defaultValue);
        Config.SetSection(key, section);
        Config.SetDesc(key, description);
        return value;
    }

    private void Update()
    {
        if (_engine == null)
            return;
        try
        {
            _engine.Tick();
        }
        catch (ThreadAbortException)
        {
            Thread.ResetAbort();
            Logging.LogWarning("[HotRepl] Stale thread abort absorbed at Update boundary.");
        }
        catch (Exception ex)
        {
            Logging.LogError($"[HotRepl] Unhandled exception in Tick(): {ex}");
        }
    }

    private void OnDestroy() => Cleanup();

    private void Cleanup()
    {
        try
        {
            _engine?.Dispose();
        }
        finally
        {
            _engine = null;
            try
            {
                _registrations?.Dispose();
            }
            finally
            {
                _registrations = null;
                StopAllCoroutines();
                UnityHelpers.Release(this);
            }
        }
    }
}
