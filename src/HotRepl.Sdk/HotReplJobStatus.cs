namespace HotRepl.Sdk;

/// <summary>Current job lifecycle state.</summary>
public sealed class HotReplJobStatus
{
    /// <summary>Create a job status snapshot.</summary>
    public HotReplJobStatus(string jobId, string state)
    {
        JobId = jobId;
        State = state;
    }

    /// <summary>Runtime job ID.</summary>
    public string JobId { get; }

    /// <summary>Wire job state, such as <c>running</c>, <c>done</c>, <c>failed</c>, or <c>cancelled</c>.</summary>
    public string State { get; }
}
