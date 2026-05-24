using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

/// <summary>Result returned after changing Time.timeScale.</summary>
public sealed class UnitySetTimeScaleResult
{
    /// <summary>Previous Time.timeScale before this call.</summary>
    [Description("Previous Time.timeScale before this call.")]
    public float PreviousTimeScale { get; set; }

    /// <summary>Time.timeScale after this call.</summary>
    [Description("Time.timeScale after this call.")]
    public float NewTimeScale { get; set; }
}
