using System;
using System.Threading;
using BepInEx;

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
[BepInPlugin(PluginGuid, PluginName, VersionInfo.PluginVersion)]
public sealed class ReplPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "hotrepl.bepinex";
    public const string PluginName = "HotRepl";
    public const string PluginVersion = VersionInfo.PluginVersion;

    private ReplEngine? _engine;

    private void Awake()
    {
        try
        {
            var host = new BepInExHost(Logger);
            _engine = new ReplEngine(host);
            _engine.Start();

            Logger.LogInfo($"{PluginName} v{VersionInfo.SemVer} loaded — REPL on port {host.Config.Port}.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"{PluginName} failed to start: {ex}");
            _engine = null;
        }
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
