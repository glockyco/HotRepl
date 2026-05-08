using HotRepl.Control.Artifacts;

namespace HotRepl.Control.Jobs;

internal sealed record ControlJob(
    string JobId,
    string RequestId,
    string? LeaseId,
    string? IdempotencyKey,
    string State
);
