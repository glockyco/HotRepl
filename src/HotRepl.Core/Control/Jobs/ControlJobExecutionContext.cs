using System;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

internal sealed record ControlJobExecutionContext(
    string JobId,
    string RequestId,
    string? LeaseId,
    string? IdempotencyKey,
    Action<JObject?, string?> Report
)
{
    public ControlCommandContext ToCommandContext(TimeSpan? timeout = null) =>
        new(RequestId, LeaseId, IdempotencyKey, timeout);
}
