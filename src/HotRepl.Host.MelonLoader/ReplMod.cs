using System;
using HotRepl.Helpers.Unity;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(
    typeof(HotRepl.Host.MelonLoader.ReplMod),
    "HotRepl",
    "0.1.0",
    "HotRepl Contributors"
)]

namespace HotRepl.Host.MelonLoader;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "MelonLoader owns the mod lifecycle; OnDeinitializeMelon disposes the engine."
)]
public sealed class ReplMod : MelonMod
{
    private ReplEngine _engine;
    private MelonLoaderHost _host;
    private CoroutineHostBehaviour _coroutineHost;

    public override void OnInitializeMelon()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<CoroutineHostBehaviour>();

            _host = new MelonLoaderHost(LoggerInstance);
            _engine = new ReplEngine(_host);
            _engine.Start();
            LoggerInstance.Msg($"HotRepl loaded — REPL on port {_host.Config.Port}.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"HotRepl failed to start: {ex}");
            _engine = null;
            _host = null;
            _coroutineHost = null;
        }
    }

    public override void OnUpdate()
    {
        if (_engine == null)
            return;

        EnsureCoroutineHost();

        try
        {
            _engine.Tick();
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"[HotRepl] Unhandled exception in Tick(): {ex}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        _engine?.Dispose();
        _engine = null;
        _host = null;
        _coroutineHost = null;
    }

    private void EnsureCoroutineHost()
    {
        if (_coroutineHost != null)
            return;

        var helperObject = new GameObject("HotRepl_CoroutineHost");
        UnityEngine.Object.DontDestroyOnLoad(helperObject);
        _coroutineHost = helperObject.AddComponent<CoroutineHostBehaviour>();
        UnityHelpers.Initialize(_coroutineHost);
    }

    public sealed class CoroutineHostBehaviour : MonoBehaviour
    {
        public CoroutineHostBehaviour(IntPtr nativePointer)
            : base(nativePointer) { }
    }
}
