using System;
using System.Collections.Generic;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Result returned by a control-plane command handler.</summary>
public sealed record ControlCommandResult(
    JObject Result,
    IReadOnlyList<ArtifactRef> Artifacts,
    IReadOnlyList<ControlCommandError> Diagnostics)
{
    public static ControlCommandResult Empty { get; } = new(new JObject(), Array.Empty<ArtifactRef>(), Array.Empty<ControlCommandError>());
}
