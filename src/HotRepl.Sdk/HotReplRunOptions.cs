using System;

namespace HotRepl.Sdk;

/// <summary>Per-command execution options.</summary>
public sealed class HotReplRunOptions
{
    /// <summary>Server-side timeout requested for this command call.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Polling interval used by job commands.</summary>
    public TimeSpan? PollingInterval { get; set; }
}
