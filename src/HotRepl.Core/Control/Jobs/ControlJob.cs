namespace HotRepl.Control.Jobs;

internal sealed record ControlJob(string JobId, string RequestId, string State);
