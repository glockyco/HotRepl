using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

internal sealed class ControlJobManager
{
    private readonly int _maxEventBuffer;
    private readonly object _sync = new();
    private readonly Dictionary<string, JobState> _jobs = new(StringComparer.Ordinal);

    public ControlJobManager(int maxEventBuffer)
    {
        _maxEventBuffer = Math.Max(1, maxEventBuffer);
    }

    public ControlJob StartJob(
        string requestId,
        string? leaseId,
        string? idempotencyKey,
        Func<ControlJobExecutionContext, CancellationToken, ValueTask<ControlCommandResult>> execute
    )
    {
        if (execute == null)
            throw new ArgumentNullException(nameof(execute));

        var jobId = Guid.NewGuid().ToString("N");
        var state = new JobState(jobId, requestId, leaseId, idempotencyKey, execute);
        lock (_sync)
        {
            _jobs.Add(jobId, state);
            AddEventLocked(state, ControlJobStates.Accepted, progress: null, message: null);
        }

        return Snapshot(state);
    }

    public async ValueTask RunAsync(string jobId)
    {
        JobState state;
        lock (_sync)
        {
            state = RequireJob(jobId);
            if (IsTerminal(state.State))
                return;
            if (state.State == ControlJobStates.Accepted)
                TransitionLocked(state, ControlJobStates.Running, message: null);
        }

        try
        {
            var context = new ControlJobExecutionContext(
                state.JobId,
                state.RequestId,
                state.LeaseId,
                state.IdempotencyKey,
                Report
            );
            var result = await state
                .Execute(context, state.Cancellation.Token)
                .ConfigureAwait(false);
            lock (_sync)
            {
                state.Result = result.Result;
                state.Artifacts = result.Artifacts.ToArray();
                state.Diagnostics = result.Diagnostics.ToArray();
                TransitionLocked(state, ControlJobStates.Completed, message: null);
            }
        }
        catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested)
        {
            lock (_sync)
                TransitionLocked(state, ControlJobStates.Cancelled, message: null);
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                state.Error = new ControlCommandError(
                    "internal",
                    "handlerException",
                    ex.Message,
                    Retryable: false,
                    Details: new JObject()
                );
                TransitionLocked(state, ControlJobStates.Failed, message: ex.Message);
            }
        }

        void Report(JObject? progress, string? message)
        {
            lock (_sync)
            {
                state.Progress = progress;
                AddEventLocked(state, state.State, progress, message);
            }
        }
    }

    public bool Cancel(string jobId)
    {
        lock (_sync)
        {
            var state = RequireJob(jobId);
            if (IsTerminal(state.State) || state.State == ControlJobStates.Cancelling)
                return false;

            TransitionLocked(state, ControlJobStates.Cancelling, message: null);
            state.Cancellation.Cancel();
            return true;
        }
    }

    public ControlJobStatus GetStatus(string jobId)
    {
        lock (_sync)
        {
            var state = RequireJob(jobId);
            return new ControlJobStatus(
                state.JobId,
                state.State,
                state.Progress,
                state.Result,
                state.Artifacts,
                state.Diagnostics,
                state.Error
            );
        }
    }

    public IReadOnlyList<ControlJobEvent> EventsAfter(string jobId, long afterSequence)
    {
        lock (_sync)
        {
            var state = RequireJob(jobId);
            return state.Events.Where(e => e.Sequence > afterSequence).ToArray();
        }
    }

    private static ControlJob Snapshot(JobState state) =>
        new(state.JobId, state.RequestId, state.LeaseId, state.IdempotencyKey, state.State);

    private JobState RequireJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
            throw new KeyNotFoundException($"Unknown control job '{jobId}'.");
        return state;
    }

    private void TransitionLocked(JobState state, string nextState, string? message)
    {
        state.State = nextState;
        AddEventLocked(state, nextState, state.Progress, message);
    }

    private void AddEventLocked(
        JobState state,
        string eventState,
        JObject? progress,
        string? message
    )
    {
        state.Events.Enqueue(
            new ControlJobEvent(state.JobId, ++state.NextSequence, eventState, progress, message)
        );
        while (state.Events.Count > _maxEventBuffer)
            state.Events.Dequeue();
    }

    private static bool IsTerminal(string state) =>
        state
            is ControlJobStates.Completed
                or ControlJobStates.Failed
                or ControlJobStates.Cancelled;

    private sealed class JobState
    {
        public JobState(
            string jobId,
            string requestId,
            string? leaseId,
            string? idempotencyKey,
            Func<
                ControlJobExecutionContext,
                CancellationToken,
                ValueTask<ControlCommandResult>
            > execute
        )
        {
            JobId = jobId;
            RequestId = requestId;
            LeaseId = leaseId;
            IdempotencyKey = idempotencyKey;
            Execute = execute;
        }

        public string JobId { get; }
        public string RequestId { get; }
        public string? LeaseId { get; }
        public string? IdempotencyKey { get; }
        public Func<
            ControlJobExecutionContext,
            CancellationToken,
            ValueTask<ControlCommandResult>
        > Execute { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public string State { get; set; } = ControlJobStates.Accepted;
        public long NextSequence { get; set; }
        public JObject? Progress { get; set; }
        public JObject? Result { get; set; }
        public ArtifactRef[] Artifacts { get; set; } = Array.Empty<ArtifactRef>();
        public ControlCommandError[] Diagnostics { get; set; } = Array.Empty<ControlCommandError>();
        public ControlCommandError? Error { get; set; }
        public Queue<ControlJobEvent> Events { get; } = new();
    }
}
