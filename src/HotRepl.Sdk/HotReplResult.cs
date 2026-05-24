using System.Collections.Generic;

namespace HotRepl.Sdk;

/// <summary>Typed command output plus top-level artifacts.</summary>
public sealed class HotReplResult<TOutput>
{
    /// <summary>Create a typed SDK result.</summary>
    public HotReplResult(TOutput output, IReadOnlyDictionary<string, Artifact> artifacts)
    {
        Output = output;
        Artifacts = artifacts;
    }

    /// <summary>Typed output payload.</summary>
    public TOutput Output { get; }

    /// <summary>Artifacts keyed by logical name.</summary>
    public IReadOnlyDictionary<string, Artifact> Artifacts { get; }
}
