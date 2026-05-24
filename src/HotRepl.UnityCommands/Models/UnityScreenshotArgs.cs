using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

/// <summary>Arguments for capturing the current frame as a PNG artifact.</summary>
public sealed class UnityScreenshotArgs
{
    /// <summary>Super-sampling factor. Values lower than 1 are clamped to 1.</summary>
    [Description("Super-sampling factor. Default 1; values lower than 1 are clamped to 1.")]
    public int SuperSize { get; set; } = 1;
}
