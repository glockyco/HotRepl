using System;

namespace HotRepl.Control.Internal;

/// <summary>No-op <see cref="IProgress{T}"/> handed to synchronous commands.</summary>
internal sealed class SilentProgress : IProgress<ControlCommandProgress>
{
    public static readonly SilentProgress Instance = new();

    private SilentProgress() { }

    public void Report(ControlCommandProgress value)
    {
        // intentionally empty
    }
}
