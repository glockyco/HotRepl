namespace HotRepl.Testing;

/// <summary>One conformance check result.</summary>
public sealed class ConformanceCheck
{
    private ConformanceCheck(string name, bool passed, string? message)
    {
        Name = name;
        Passed = passed;
        Message = message;
    }

    /// <summary>Check name.</summary>
    public string Name { get; }

    /// <summary>True when this check passed.</summary>
    public bool Passed { get; }

    /// <summary>Failure message when present.</summary>
    public string? Message { get; }

    /// <summary>Create a passing check.</summary>
    public static ConformanceCheck Pass(string name) => new(name, passed: true, message: null);

    /// <summary>Create a failing check.</summary>
    public static ConformanceCheck Fail(string name, string message) =>
        new(name, passed: false, message: message);
}
