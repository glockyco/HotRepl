using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HotRepl.Evaluator.MonoCSharp;

/// <summary>
/// Determines which assemblies should NOT be referenced in the Mono evaluator session.
///
/// Filters three categories:
/// 1. mcs autocomplete artifacts ("completions")
/// 2. Stdlib duplicates implicitly loaded by Mono.CSharp.Evaluator
/// 3. Superseded hot-reload assemblies (older ScriptEngine versions)
///
/// ScriptEngine (BepInEx) renames assemblies on each F6 reload using the pattern
/// "{BaseName}-{DateTime.Now.Ticks}". When multiple versions coexist in the
/// AppDomain, only the newest (highest ticks) should be referenced. Older
/// versions are filtered out so Mono.CSharp resolves types from the live code.
/// </summary>
public static class AssemblyFilter
{
    // Case-insensitive — assembly names can arrive in any casing depending on loader.
    internal static readonly HashSet<string> FilteredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // mcs autocomplete artifact
        "completions",
        // Stdlib duplicates implicitly loaded by Mono.CSharp.Evaluator
        "mscorlib",
        "System",
        "System.Core",
        "System.Xml",
        "System.Xml.Linq",
        "System.Data",
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Threading",
        "System.IO",
        "System.Text",
        "System.Net",
        "Microsoft.CSharp",
        "netstandard",
    };

    // ScriptEngine naming: {BaseName}-{Ticks} where Ticks is DateTime.Now.Ticks.
    // Ticks is a 17-19 digit integer; values below ~630000000000000000 (~year 2000)
    // are implausible timestamps, avoiding false positives on assembly names that
    // happen to end with a dash and digits.
    private static readonly Regex ScriptEnginePattern = new(
        @"^(.+)-(\d{17,19})$",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1)
    );
    private const long MinPlausibleTicks = 630000000000000000L; // ~year 2000

    /// <summary>Returns true when the named assembly should be skipped during referencing.</summary>
    public static bool IsFiltered(string name) => FilteredNames.Contains(name);

    /// <summary>
    /// Parse a ScriptEngine-style assembly name into its base name and ticks.
    /// Returns false if the name doesn't match the pattern.
    /// </summary>
    public static bool TryParseScriptEngineName(string name, out string baseName, out long ticks)
    {
        var match = ScriptEnginePattern.Match(name);
        if (
            match.Success
            && long.TryParse(
                match.Groups[2].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ticks
            )
            && ticks > MinPlausibleTicks
        )
        {
            baseName = match.Groups[1].Value;
            return true;
        }
        baseName = name;
        ticks = 0;
        return false;
    }

    /// <summary>
    /// Check if an assembly is superseded by a newer ScriptEngine version
    /// already loaded in the AppDomain. Returns true if a newer version exists
    /// (meaning this assembly should NOT be referenced).
    /// </summary>
    public static bool IsSuperseded(Assembly candidate, Assembly[] allAssemblies)
    {
        var candidateName = candidate.GetName().Name;
        if (string.IsNullOrEmpty(candidateName))
            return false;

        if (!TryParseScriptEngineName(candidateName, out var baseName, out var candidateTicks))
            return false;

        // Check if any other assembly has the same base name but higher ticks.
        foreach (var asm in allAssemblies)
        {
            if (ReferenceEquals(asm, candidate))
                continue;
            var otherName = asm.GetName().Name;
            if (string.IsNullOrEmpty(otherName))
                continue;
            if (
                TryParseScriptEngineName(otherName, out var otherBase, out var otherTicks)
                && string.Equals(baseName, otherBase, StringComparison.OrdinalIgnoreCase)
                && otherTicks > candidateTicks
            )
            {
                return true;
            }
        }

        return false;
    }
}
