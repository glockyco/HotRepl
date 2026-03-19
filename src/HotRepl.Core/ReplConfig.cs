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
}
