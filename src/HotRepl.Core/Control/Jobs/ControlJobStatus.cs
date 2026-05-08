using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

internal sealed record ControlJobStatus(
    string JobId,
    string State,
    JObject? Progress,
    JObject? Result,
    ArtifactRef[] Artifacts,
    ControlCommandError[] Diagnostics,
    ControlCommandError? Error
);
