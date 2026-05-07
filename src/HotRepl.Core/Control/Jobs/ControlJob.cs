using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

internal static class ControlJobStates
{
    public const string Accepted = "accepted";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelling = "cancelling";
    public const string Cancelled = "cancelled";
}

internal sealed record ControlJob(
    string JobId,
    string RequestId,
    string? LeaseId,
    string? IdempotencyKey,
    string State);

internal sealed record ControlJobStatus(
    string JobId,
    string State,
    JObject? Progress,
    JObject? Result,
    ArtifactRef[] Artifacts,
    ControlCommandError[] Diagnostics,
    ControlCommandError? Error);
