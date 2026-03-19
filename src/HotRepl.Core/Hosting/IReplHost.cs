using System;
using System.Collections.Generic;
using System.Reflection;

namespace HotRepl.Hosting
{
    /// <summary>
    /// Abstraction over the host environment (game engine, test harness, etc.)
    /// that provides assemblies, usings, and logging.
    /// </summary>
    public interface IReplHost
    {
        /// <summary>
        /// Assemblies the evaluator should reference on initialization.
        /// Typically the game assembly plus its dependencies.
        /// </summary>
        IReadOnlyList<Assembly> ReferenceAssemblies { get; }

        /// <summary>
        /// Namespace imports applied to every evaluation by default
        /// (e.g. "System", "System.Linq", "UnityEngine").
        /// </summary>
        IReadOnlyList<string> DefaultUsings { get; }

        /// <summary>
        /// Emits a log message through the host's logging infrastructure.
        /// </summary>
        void Log(LogLevel level, string message);
    }

    /// <summary>
    /// Severity levels for <see cref="IReplHost.Log"/>.
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
