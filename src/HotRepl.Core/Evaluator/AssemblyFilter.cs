using System;
using System.Collections.Generic;

namespace HotRepl.Evaluator;

/// <summary>
/// Determines which assemblies should NOT be referenced in the Mono evaluator session.
/// Filtered names are either mcs autocomplete artifacts or stdlib assemblies the
/// evaluator already loads implicitly (adding them twice causes duplicate-symbol errors
/// in some Mono versions).
/// </summary>
internal static class AssemblyFilter
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

    /// <summary>Returns true when the named assembly should be skipped during referencing.</summary>
    internal static bool IsFiltered(string name) => FilteredNames.Contains(name);
}
