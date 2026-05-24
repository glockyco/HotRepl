using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Handle for a running HotRepl job command.</summary>
public sealed class HotReplJob<TResult>
{
    private readonly TimeSpan _defaultPollInterval;
    private readonly HotReplSession _session;
    private HotReplJobStatus _lastKnownStatus;

    internal HotReplJob(
        HotReplSession session,
        string id,
        string initialState,
        TimeSpan defaultPollInterval
    )
    {
        _session = session;
        Id = id;
        _defaultPollInterval = defaultPollInterval;
        _lastKnownStatus = new HotReplJobStatus(id, initialState);
    }

    /// <summary>Runtime job ID.</summary>
    public string Id { get; }

    /// <summary>Most recent status observed by this SDK object.</summary>
    public HotReplJobStatus LastKnownStatus => _lastKnownStatus;

    /// <summary>Poll progress until the job reaches a terminal state.</summary>
    public async IAsyncEnumerable<HotReplJobProgress> ProgressAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        while (true)
        {
            var (status, raw) = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            _lastKnownStatus = status;
            var progress = raw["progress"] as JObject;
            if (progress is not null)
            {
                yield return new HotReplJobProgress(
                    progress["snapshot"] as JObject,
                    (string?)progress["message"]
                );
            }

            if (!string.Equals(status.State, "running", StringComparison.Ordinal))
            {
                yield break;
            }

            await Task.Delay(_defaultPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Poll current job status once.</summary>
    public async Task<HotReplJobStatus> GetStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        var (status, _) = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
        _lastKnownStatus = status;
        return status;
    }

    /// <summary>Request job cancellation.</summary>
    public Task CancelAsync(CancellationToken cancellationToken = default)
    {
        var id = _session.NextId("cancel");
        return _session.RequestRawAsync(
            new JObject
            {
                ["type"] = "job_cancel",
                ["id"] = id,
                ["jobId"] = Id,
            },
            cancellationToken
        );
    }

    /// <summary>Wait until the job completes and parse the terminal result.</summary>
    public Task<HotReplResult<TResult>> WaitForCompletionAsync(
        CancellationToken cancellationToken = default
    ) => WaitForCompletionAsync(_defaultPollInterval, cancellationToken);

    /// <summary>Wait until the job completes and parse the terminal result.</summary>
    public async Task<HotReplResult<TResult>> WaitForCompletionAsync(
        TimeSpan pollingInterval,
        CancellationToken cancellationToken = default
    )
    {
        while (true)
        {
            var (status, raw) = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            _lastKnownStatus = status;
            if (string.Equals(status.State, "running", StringComparison.Ordinal))
            {
                await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return _session.ParseJobTerminal<TResult>(raw, Id);
        }
    }

    private async Task<(HotReplJobStatus Status, JObject Raw)> PollOnceAsync(
        CancellationToken cancellationToken
    )
    {
        var id = _session.NextId("status");
        var raw = await _session
            .RequestRawAsync(
                new JObject
                {
                    ["type"] = "job_status",
                    ["id"] = id,
                    ["jobId"] = Id,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        var state = (string?)raw["state"] ?? "running";
        return (new HotReplJobStatus(Id, state), raw);
    }
}
