using System;
using System.Linq;
using System.Reflection;
using HotRepl.Evaluator;

namespace HotRepl.Helpers;

/// <summary>
/// Injects the Repl helper class and any host-provided additional helpers into
/// an evaluator session. Also computes the helpers[] array for the handshake.
///
/// The Core helper signatures are derived once via reflection at class-load time
/// and cached — no runtime compilation involved.
/// </summary>
internal static class HelperInjector
{
    /// <summary>
    /// Signatures of public Repl methods, ready for the handshake helpers[] field.
    /// Computed once at startup from the compiled Repl type — always in sync.
    /// </summary>
    public static readonly string[] CoreHelperSignatures = BuildSignatures(typeof(Repl));

    /// <summary>
    /// Injects the Core Repl class and any host-provided additional assemblies/usings
    /// into the evaluator session. Must be called during Initialize() / Reset().
    /// </summary>
    public static void Inject(ICodeEvaluator evaluator, IReplHost host, HistoryTracker history, ReplConfig config)
    {
        // Bind runtime services so Repl.Help(), Repl.History(), etc. work.
        Repl.Initialize(history, config.MaxEnumerableElements);

        // Make the compiled Repl type available in the session.
        evaluator.ReferenceAssembly(typeof(Repl).Assembly);
        evaluator.RunInternal("using HotRepl.Helpers;");

        // Host-provided extras (e.g. HotRepl.BepInEx containing UnityHelpers).
        foreach (var asm in host.AdditionalAssemblies)
            evaluator.ReferenceAssembly(asm);
        foreach (var ns in host.AdditionalUsings)
            evaluator.RunInternal($"using {ns};");
    }

    /// <summary>
    /// Combines Core and host-provided helper signatures for the handshake.
    /// </summary>
    public static string[] AllHelperSignatures(IReplHost host)
    {
        var additional = host.AdditionalHelperSignatures;
        if (additional == null || additional.Length == 0)
            return CoreHelperSignatures;

        var combined = new string[CoreHelperSignatures.Length + additional.Length];
        Array.Copy(CoreHelperSignatures, combined, CoreHelperSignatures.Length);
        Array.Copy(additional, 0, combined, CoreHelperSignatures.Length, additional.Length);
        return combined;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    internal static string[] BuildSignatures(Type type)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.DeclaringType == type && !m.IsSpecialName && !m.Name.StartsWith("__", StringComparison.Ordinal))
            .Select(FormatSignature)
            .ToArray();
    }

    private static string FormatSignature(MethodInfo m)
    {
        var ps = m.GetParameters();
        var pstr = string.Join(", ", Array.ConvertAll(ps, p =>
        {
            var part = $"{p.ParameterType.Name} {p.Name}";
            if (p.HasDefaultValue)
                part += $" = {p.DefaultValue ?? "null"}";
            return part;
        }));
        return $"{m.ReturnType.Name} {m.Name}({pstr})";
    }
}
