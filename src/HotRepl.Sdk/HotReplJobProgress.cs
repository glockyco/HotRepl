using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Progress snapshot emitted by a running HotRepl job.</summary>
public sealed class HotReplJobProgress
{
    /// <summary>Create a job progress update.</summary>
    public HotReplJobProgress(JObject? snapshot, string? message)
    {
        Snapshot = snapshot;
        Message = message;
    }

    /// <summary>Optional structured progress snapshot.</summary>
    public JObject? Snapshot { get; }

    /// <summary>Optional human-readable progress message.</summary>
    public string? Message { get; }
}
