using System.Collections.Concurrent;
using System.Reflection;
using BepInEx.Logging;
using HotRepl.Hosting;

namespace HotRepl.BepInEx;

/// <summary>
/// IReplHost implementation for BepInEx 5.x on Unity Mono.
/// Bridges the game's main thread (Update) and BepInEx logging.
/// </summary>
internal sealed class BepInExHost : IReplHost
{
    private static readonly HashSet<string> StdLib = new(StringComparer.OrdinalIgnoreCase)
    {
        "mscorlib", "System.Core", "System", "System.Xml",
    };

    private readonly ManualLogSource _logger;
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    public BepInExHost(ManualLogSource logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<Assembly> ReferenceAssemblies
    {
        get
        {
            var result = new List<Assembly>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name == null || StdLib.Contains(name))
                    continue;
                result.Add(asm);
            }
            return result;
        }
    }

    public IReadOnlyList<string> DefaultUsings => new[]
    {
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Linq",
        "System.Reflection",
        "UnityEngine",
        "UnityEngine.SceneManagement",
    };

    public void RunOnMainThread(Action action)
    {
        _mainThreadQueue.Enqueue(action);
    }

    public void Log(Hosting.LogLevel level, string message)
    {
        var bepLevel = level switch
        {
            Hosting.LogLevel.Debug => global::BepInEx.Logging.LogLevel.Debug,
            Hosting.LogLevel.Info => global::BepInEx.Logging.LogLevel.Info,
            Hosting.LogLevel.Warning => global::BepInEx.Logging.LogLevel.Warning,
            Hosting.LogLevel.Error => global::BepInEx.Logging.LogLevel.Error,
            _ => global::BepInEx.Logging.LogLevel.Info,
        };
        _logger.Log(bepLevel, message);
    }

    /// <summary>
    /// Drain queued main-thread actions. Called from Plugin.Update().
    /// </summary>
    internal void DrainMainThread()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Main thread action failed: {ex}");
            }
        }
    }
}
