using System;
using System.Collections.Generic;

namespace HotRepl.Evaluation
{
    /// <summary>
    /// Canonical set of standard-library assembly names that should be excluded
    /// when gathering reference assemblies. Re-importing these after the Mono
    /// evaluator has already loaded them causes duplicate-type errors.
    /// </summary>
    internal static class AssemblyFilter
    {
        internal static readonly HashSet<string> StdLibNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "mscorlib",
            "System",
            "System.Core",
            "System.Xml"
        };
    }
}
