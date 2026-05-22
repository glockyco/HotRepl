namespace HotRepl.Protocol;

/// <summary>Names of runtime limits that are actively enforced.</summary>
public static class EnforcedLimit
{
    /// <summary>Inbound frame size limit.</summary>
    public const string MaxMessageBytes = "maxMessageBytes";

    /// <summary>Queued command count limit.</summary>
    public const string MaxQueuedCommands = "maxQueuedCommands";

    /// <summary>Serialized result length limit.</summary>
    public const string MaxResultLength = "maxResultLength";

    /// <summary>Serialized enumerable element count limit.</summary>
    public const string MaxEnumerableElements = "maxEnumerableElements";

    /// <summary>Concurrent job count limit.</summary>
    public const string MaxJobConcurrency = "maxJobConcurrency";

    /// <summary>Default enforced v2 limits.</summary>
    public static string[] Defaults { get; } =
    {
        MaxMessageBytes,
        MaxQueuedCommands,
        MaxResultLength,
        MaxEnumerableElements,
        MaxJobConcurrency,
    };
}
