using BepInEx;
using HotRepl.Hosting;

namespace HotRepl.BepInEx;

/// <summary>
/// BepInEx plugin entry point for HotRepl.
/// Starts a WebSocket REPL server that accepts C# code for runtime evaluation.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ReplPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.hotrepl.bepinex";
    public const string PluginName = "HotRepl";
    public const string PluginVersion = "0.1.0";

    private BepInExHost? _host;
    private ReplEngine? _engine;

    private void Awake()
    {
        _host = new BepInExHost(Logger);
        _engine = new ReplEngine(_host, new ReplConfig { Port = 18590 });
        _engine.Start();

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded — REPL server on port 18590");
    }

    private void Update()
    {
        _host?.DrainMainThread();
        _engine?.Tick();
    }

    private void OnDestroy()
    {
        _engine?.Dispose();
    }
}
