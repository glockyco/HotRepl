#nullable disable

using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

/// <summary>Result of a GameObject lookup.</summary>
public sealed class UnityGameObjectFindResult
{
    /// <summary>Null when no GameObject matched the requested path.</summary>
    [Description("Null when no GameObject matched the requested path.")]
    public UnityGameObject GameObject { get; set; }
}
