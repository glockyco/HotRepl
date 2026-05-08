using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

internal sealed record ControlJobEvent(
    string JobId,
    long Sequence,
    string State,
    JObject? Progress,
    string? Message
);
