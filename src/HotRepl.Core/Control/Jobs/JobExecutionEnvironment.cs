using System;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

/// <summary>
/// Runtime context passed by <see cref="ControlJobManager"/> to its execute
/// delegate. Carries the job ID, the raw progress callback the manager uses
/// to record events, and the per-job artifact writer.
/// </summary>
internal readonly struct JobExecutionEnvironment : IEquatable<JobExecutionEnvironment>
{
    public JobExecutionEnvironment(
        string jobId,
        Action<JObject?, string?> progressSink,
        IArtifactWriter artifacts
    )
    {
        JobId = jobId;
        ProgressSink = progressSink;
        Artifacts = artifacts;
    }

    public string JobId { get; }
    public Action<JObject?, string?> ProgressSink { get; }
    public IArtifactWriter Artifacts { get; }

    /// <inheritdoc />
    public bool Equals(JobExecutionEnvironment other) =>
        string.Equals(JobId, other.JobId, StringComparison.Ordinal)
        && ReferenceEquals(ProgressSink, other.ProgressSink)
        && ReferenceEquals(Artifacts, other.Artifacts);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is JobExecutionEnvironment o && Equals(o);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(JobId, ProgressSink, Artifacts);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(JobExecutionEnvironment left, JobExecutionEnvironment right) =>
        left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(JobExecutionEnvironment left, JobExecutionEnvironment right) =>
        !left.Equals(right);
}
