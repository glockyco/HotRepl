using System.Collections.Generic;

namespace HotRepl.Testing;

/// <summary>Aggregate conformance result.</summary>
public sealed class ConformanceResult
{
    /// <summary>Create a conformance result.</summary>
    public ConformanceResult(IReadOnlyList<ConformanceCheck> checks)
    {
        Checks = checks;
    }

    /// <summary>All checks that ran.</summary>
    public IReadOnlyList<ConformanceCheck> Checks { get; }

    /// <summary>True when every check passed.</summary>
    public bool Passed
    {
        get
        {
            foreach (var check in Checks)
            {
                if (!check.Passed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
