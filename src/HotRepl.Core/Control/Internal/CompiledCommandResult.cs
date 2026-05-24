using System;
using System.Collections.Generic;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Internal record consumed by the router. Mirrors the wire shape:
/// status, output, top-level artifact list, top-level diagnostic list.
/// </summary>
internal sealed record CompiledCommandResult(
    bool Succeeded,
    JObject Output,
    IReadOnlyList<ArtifactRef> Artifacts,
    IReadOnlyList<ControlCommandError> Diagnostics
)
{
    public static CompiledCommandResult Empty { get; } =
        new(
            Succeeded: true,
            Output: new JObject(),
            Artifacts: Array.Empty<ArtifactRef>(),
            Diagnostics: Array.Empty<ControlCommandError>()
        );
}
