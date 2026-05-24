using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HotRepl.UnityCommands.Models;

/// <summary>Arguments for setting UnityEngine.Time.timeScale.</summary>
public sealed class UnitySetTimeScaleArgs
{
    /// <summary>New Time.timeScale value.</summary>
    [Required]
    [Range(0f, 100f)]
    [Description(
        "New Time.timeScale value. 0 = paused, 1 = normal, 2 = double-speed. Values greater than 1 may exceed safe physics-step bounds in some games."
    )]
    public float TimeScale { get; set; }
}
