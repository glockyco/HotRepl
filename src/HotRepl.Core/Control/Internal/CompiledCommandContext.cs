using System;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Internal context handed to <see cref="ICompiledControlCommand"/>
/// implementations. Carries the raw progress callback the job manager
/// wants and the artifact writer instance the adapter projects into the
/// result.
/// </summary>
internal sealed class CompiledCommandContext
{
    public CompiledCommandContext(
        string requestId,
        TimeSpan? timeout,
        string? jobId,
        Action<JObject?, string?>? progressSink,
        IArtifactWriter artifacts
    )
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Timeout = timeout;
        JobId = jobId;
        ProgressSink = progressSink;
        Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    public string RequestId { get; }
    public TimeSpan? Timeout { get; }
    public string? JobId { get; }

    /// <summary>Null for synchronous commands; non-null for jobs.</summary>
    public Action<JObject?, string?>? ProgressSink { get; }
    public IArtifactWriter Artifacts { get; }
}
