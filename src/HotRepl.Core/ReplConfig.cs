namespace HotRepl;

/// <summary>
/// Configuration for the REPL engine and WebSocket server.
/// All properties have safe defaults; override only what you need.
/// </summary>
public sealed class ReplConfig
{
    /// <summary>WebSocket listen port. Default: 18590.</summary>
    public int Port { get; set; } = 18590;

    /// <summary>
    /// Optional evaluator name override. When null, the host chooses its safe default.
    /// </summary>
    public string? DefaultEvaluatorName { get; set; }

    /// <summary>
    /// Wall-clock budget (ms) per evaluation before the watchdog aborts the thread.
    /// Default: 10 000 ms.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 10_000;

    /// <summary>
    /// Maximum character length of a serialized result value before truncation.
    /// Default: 100 000.
    /// </summary>
    public int MaxResultLength { get; set; } = 100_000;

    /// <summary>
    /// Maximum number of elements enumerated when serializing IEnumerable results.
    /// Default: 100.
    /// </summary>
    public int MaxEnumerableElements { get; set; } = 100;

    /// <summary>Enable the typed command/job control-plane protocol. Default: true.</summary>
    public bool ControlPlaneEnabled { get; set; } = true;

    /// <summary>WebSocket bind host. Default: loopback only.</summary>
    public string BindHost { get; set; } = "127.0.0.1";

    /// <summary>Whether control-plane auth is required. Default: false until auth is configured.</summary>
    public bool RequireControlAuth { get; set; }

    /// <summary>Optional token required for control-plane authentication.</summary>
    public string? ControlAuthToken { get; set; }

    /// <summary>Whether mutating control commands require an exclusive lease. Default: true.</summary>
    public bool RequireControlLease { get; set; } = true;

    /// <summary>Maximum inbound control-plane message size in bytes. Default: 1 MiB.</summary>
    public int MaxControlMessageBytes { get; set; } = 1024 * 1024;

    /// <summary>Maximum number of queued control commands before overload rejection. Default: 32.</summary>
    public int MaxQueuedControlCommands { get; set; } = 32;

    /// <summary>Maximum buffered event count per control-plane job. Default: 1000.</summary>
    public int MaxJobEventBuffer { get; set; } = 1000;
}
