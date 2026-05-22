using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Runtime limits advertised in the v2 handshake.</summary>
public sealed class RuntimeLimits
{
    /// <summary>Default inbound frame limit in bytes.</summary>
    public const int DefaultMaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>Default serialized result length limit in bytes.</summary>
    public const int DefaultMaxResultLength = 100 * 1024;

    /// <summary>Default enumerable element serialization limit.</summary>
    public const int DefaultMaxEnumerableElements = 100;

    /// <summary>Default queued command limit.</summary>
    public const int DefaultMaxQueuedCommands = 32;

    /// <summary>Default eval timeout in milliseconds.</summary>
    public const int DefaultEvalTimeoutMilliseconds = 10_000;

    /// <summary>Default maximum concurrent job count.</summary>
    public const int DefaultMaxJobConcurrency = 1;

    /// <summary>Creates the default v2 runtime limits.</summary>
    public static RuntimeLimits CreateDefault() =>
        new()
        {
            MaxMessageBytes = DefaultMaxMessageBytes,
            MaxQueuedCommands = DefaultMaxQueuedCommands,
            MaxResultLength = DefaultMaxResultLength,
            MaxEnumerableElements = DefaultMaxEnumerableElements,
            DefaultEvalTimeoutMs = DefaultEvalTimeoutMilliseconds,
            MaxJobConcurrency = DefaultMaxJobConcurrency,
        };

    /// <summary>Maximum inbound frame size in bytes.</summary>
    [JsonProperty("maxMessageBytes")]
    public int MaxMessageBytes { get; set; }

    /// <summary>Maximum queued command count.</summary>
    [JsonProperty("maxQueuedCommands")]
    public int MaxQueuedCommands { get; set; }

    /// <summary>Maximum serialized result length in bytes.</summary>
    [JsonProperty("maxResultLength")]
    public int MaxResultLength { get; set; }

    /// <summary>Maximum serialized enumerable element count.</summary>
    [JsonProperty("maxEnumerableElements")]
    public int MaxEnumerableElements { get; set; }

    /// <summary>Default eval timeout in milliseconds.</summary>
    [JsonProperty("defaultEvalTimeoutMs")]
    public int DefaultEvalTimeoutMs { get; set; }

    /// <summary>Maximum number of concurrently running jobs.</summary>
    [JsonProperty("maxJobConcurrency")]
    public int MaxJobConcurrency { get; set; }
}
