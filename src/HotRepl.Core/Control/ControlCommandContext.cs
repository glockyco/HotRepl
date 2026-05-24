using System;
using HotRepl.Control.Artifacts;

namespace HotRepl.Control;

/// <summary>Per-invocation context handed to a typed command handler.</summary>
public class ControlCommandContext
{
    /// <summary>Construct a context. Hosts and the adapter populate this; handlers receive it.</summary>
    public ControlCommandContext(
        string requestId,
        TimeSpan? timeout,
        string? jobId,
        IProgress<ControlCommandProgress> progress,
        IArtifactWriter artifacts
    )
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Timeout = timeout;
        JobId = jobId;
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    /// <summary>Originating wire request ID. Use for log correlation only.</summary>
    public string RequestId { get; }

    /// <summary>Caller-requested timeout. Null means no explicit caller timeout.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>Job ID, if this command is a job. Null for synchronous commands.</summary>
    public string? JobId { get; }

    /// <summary>
    /// Progress sink. For synchronous commands, <see cref="IProgress{T}.Report"/>
    /// calls are silently dropped. For job commands, each call becomes a
    /// <c>job_status_result</c> snapshot and an event in the job event buffer.
    /// </summary>
    public IProgress<ControlCommandProgress> Progress { get; }

    /// <summary>
    /// Artifact writer. Calls are idempotent on logical name (a second write
    /// with the same name replaces the first).
    /// </summary>
    public IArtifactWriter Artifacts { get; }
}
