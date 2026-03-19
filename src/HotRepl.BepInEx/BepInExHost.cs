using System.Reflection;
using BepInEx.Logging;
using HotRepl.Evaluation;
using HotRepl.Hosting;

namespace HotRepl.BepInEx;

/// <summary>
/// IReplHost implementation for BepInEx 5.x on Unity Mono.
/// Bridges BepInEx logging to the REPL host abstraction.
/// </summary>
internal sealed class BepInExHost : IReplHost
{

    private readonly ManualLogSource _logger;
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
                if (name == null || AssemblyFilter.StdLibNames.Contains(name))
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
}
