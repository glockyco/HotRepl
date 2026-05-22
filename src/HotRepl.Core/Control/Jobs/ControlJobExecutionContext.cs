using System;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

internal sealed record ControlJobExecutionContext(
    string JobId,
    string RequestId,
    Action<JObject?, string?> Report
)
{
    public ControlCommandContext ToCommandContext(TimeSpan? timeout = null) => new(RequestId, timeout);
}
