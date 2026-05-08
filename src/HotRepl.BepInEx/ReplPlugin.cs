using System;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using HotRepl.Helpers.Unity;
using UnityEngine;

namespace HotRepl.BepInEx;

/// <summary>
/// BepInEx 5.x plugin entry point.
/// Wires the Core ReplEngine to Unity's MonoBehaviour lifecycle.
///
/// Awake()  — fast: installs stdout capture, binds the WebSocket port.
///            Zero C# compilation.
/// Update() — calls Tick() once per frame; initializes the evaluator on the
///            very first call (deferred from Awake for startup speed).
/// </summary>
[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
public sealed class ReplPlugin : BaseUnityPlugin
{
    private ReplEngine? _engine;

    private void Awake()
    {
        try
        {
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            var config = LoadConfig();
            var host = new BepInExHost(Logger, config);
            _engine = new ReplEngine(host);
            UnityHelpers.Initialize(this);
            _engine.Start();

            Logger.LogInfo(
                $"{PluginInfo.Name} v{PluginInfo.Version} loaded — REPL on {host.Config.BindHost}:{host.Config.Port}."
            );
        }
        catch (Exception ex)
        {
            Logger.LogError($"{PluginInfo.Name} failed to start: {ex}");
            _engine = null;
        }
    }

    private ReplConfig LoadConfig()
    {
        var port = Config.Bind("Server", "Port", 18590, "WebSocket listen port.");
        var bindHost = Config.Bind(
            "Server",
            "BindHost",
            "127.0.0.1",
            "WebSocket bind host. Use 127.0.0.1 for loopback-only, or explicitly set 0.0.0.0 for host-reachable automation."
        );
        var requireAuth = Config.Bind(
            "Control",
            "RequireAuth",
            false,
            "Require control-plane authentication."
        );
        var authToken = Config.Bind(
            "Control",
            "AuthToken",
            string.Empty,
            "Token required for control-plane authentication. Set this when BindHost is not loopback."
        );

        var config = new ReplConfig
        {
            Port = port.Value,
            BindHost = bindHost.Value,
            RequireControlAuth = requireAuth.Value,
            ControlAuthToken = string.IsNullOrWhiteSpace(authToken.Value) ? null : authToken.Value,
        };

        ReplConfigExposurePolicy.ApplyControlAuthToken(config);
        foreach (var warning in ReplConfigExposurePolicy.Validate(config).Warnings)
            Logger.LogWarning(warning);

        return config;
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
            // Last-resort guard: a stale watchdog abort that escaped Tick().
            // ResetAbort so Unity's Update loop doesn't propagate the exception.
            Thread.ResetAbort();
            Logger.LogWarning("[HotRepl] Stale thread abort absorbed at Update boundary.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[HotRepl] Unhandled exception in Tick(): {ex}");
        }
    }

    private void OnDestroy()
    {
        _engine?.Dispose();
        _engine = null;
    }
}
