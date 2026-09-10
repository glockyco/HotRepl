using System;
using System.Globalization;
using HotRepl.Storage;

namespace HotRepl;

/// <summary>
/// Configuration for the REPL engine and WebSocket server.
/// All properties have safe defaults; override only what you need.
/// </summary>
public sealed class ReplConfig
{
    /// <summary>Environment variable that overrides <see cref="Port"/> for one process.</summary>
    public const string PortVariable = "HOTREPL_PORT";

    /// <summary>
    /// Environment variable that overrides <see cref="ArtifactDirectory"/> for one process.
    /// </summary>
    public const string ArtifactDirectoryVariable = "HOTREPL_ARTIFACT_DIR";

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

    /// <summary>WebSocket bind host. Default: loopback only.</summary>
    public string BindHost { get; set; } = "127.0.0.1";

    /// <summary>Maximum inbound protocol message size in bytes. Default: 4 MiB.</summary>
    public int MaxMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Maximum number of queued protocol commands before overload rejection. Default: 32.</summary>
    public int MaxQueuedCommands { get; set; } = 32;

    /// <summary>Maximum number of concurrently running command jobs. Default: 1.</summary>
    public int MaxJobConcurrency { get; set; } = 1;

    /// <summary>Maximum buffered event count per control-plane job. Default: 1000.</summary>
    public int MaxJobEventBuffer { get; set; } = 1000;

    /// <summary>
    /// Directory that holds command artifacts. A command attaches bytes, the engine writes them
    /// here, and the reference it returns carries this path so a client on the same machine reads
    /// the file. Default: the per-user state directory.
    /// </summary>
    /// <remarks>
    /// A game that runs under Wine or Proton sees a Windows path. Point this at a mapped drive when
    /// the client reads artifacts from the host filesystem.
    /// </remarks>
    public string ArtifactDirectory { get; set; } = HotReplStateDirectory.Resolve("artifacts");

    /// <summary>
    /// Applies per-process overrides from the environment and returns this instance.
    /// </summary>
    /// <remarks>
    /// The environment carries these values because they differ per process, where a configuration
    /// file is read identically by every process that loads the same host.
    /// </remarks>
    public ReplConfig ApplyEnvironmentOverrides()
    {
        if (TryParsePort(Environment.GetEnvironmentVariable(PortVariable), out var port))
            Port = port;

        var directory = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(directory))
            ArtifactDirectory = directory!.Trim();

        return this;
    }

    /// <summary>
    /// Parses a listen port. Returns false for absent, malformed, or out-of-range text, so a
    /// mistyped variable keeps the default rather than failing the host at startup.
    /// </summary>
    public static bool TryParsePort(string? value, out int port)
    {
        port = 0;
        if (
            !int.TryParse(
                value?.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
            return false;

        if (parsed is < 1 or > 65535)
            return false;

        port = parsed;
        return true;
    }
}
