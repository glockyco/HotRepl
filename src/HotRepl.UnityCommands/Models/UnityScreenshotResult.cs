using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

/// <summary>Metadata for a captured screenshot artifact.</summary>
public sealed class UnityScreenshotResult
{
    /// <summary>Width of the captured frame in pixels.</summary>
    [Description("Width of the captured frame in pixels.")]
    public int Width { get; set; }

    /// <summary>Height of the captured frame in pixels.</summary>
    [Description("Height of the captured frame in pixels.")]
    public int Height { get; set; }
}
