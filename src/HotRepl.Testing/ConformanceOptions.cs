namespace HotRepl.Testing;

/// <summary>Options for the SDK conformance smoke suite.</summary>
public sealed class ConformanceOptions
{
    /// <summary>Whether command descriptors are required for every catalog entry.</summary>
    public bool DescribeEveryCommand { get; set; } = true;
}
