using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

/// <summary>Basic Unity application/runtime metadata.</summary>
public sealed class UnityAppInfo
{
    /// <summary>Application.productName as configured in the Unity project.</summary>
    [Description("Application.productName as configured in the Unity project.")]
    public string ProductName { get; set; } = "";

    /// <summary>Unity engine version the game was built with.</summary>
    [Description("Unity engine version the game was built with.")]
    public string UnityVersion { get; set; } = "";

    /// <summary>RuntimePlatform enum value as a string.</summary>
    [Description("RuntimePlatform enum value as a string.")]
    public string Platform { get; set; } = "";

    /// <summary>True when running in the Unity Editor.</summary>
    [Description("True when running in the Unity Editor; false in player builds.")]
    public bool IsEditor { get; set; }
}
