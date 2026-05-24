using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HotRepl.UnityCommands.Models;

/// <summary>Arguments for locating a GameObject in the active Unity scenes.</summary>
public sealed class UnityGameObjectFindArgs
{
    /// <summary>Hierarchy path or plain GameObject name.</summary>
    [Required]
    [Description(
        "Hierarchy path. Plain names and slash-separated Unity GameObject.Find paths are accepted."
    )]
    public string Path { get; set; } = "";
}
