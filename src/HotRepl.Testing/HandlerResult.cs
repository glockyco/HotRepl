using System.Collections.Generic;
using HotRepl.Control;
using HotRepl.Control.Artifacts;

namespace HotRepl.Testing;

/// <summary>Captured result from an in-process handler execution.</summary>
public sealed class HandlerResult<TOutput>
{
    /// <summary>Create a captured handler result.</summary>
    public HandlerResult(
        bool succeeded,
        TOutput? output,
        IReadOnlyDictionary<string, ArtifactRef> artifacts,
        IReadOnlyList<ControlCommandDiagnostic> diagnostics
    )
    {
        Succeeded = succeeded;
        Output = output;
        Artifacts = artifacts;
        Diagnostics = diagnostics;
    }

    /// <summary>True when the handler reported success.</summary>
    public bool Succeeded { get; }

    /// <summary>Typed output payload.</summary>
    public TOutput? Output { get; }

    /// <summary>Artifacts produced by the handler.</summary>
    public IReadOnlyDictionary<string, ArtifactRef> Artifacts { get; }

    /// <summary>Diagnostics produced by the handler.</summary>
    public IReadOnlyList<ControlCommandDiagnostic> Diagnostics { get; }
}
