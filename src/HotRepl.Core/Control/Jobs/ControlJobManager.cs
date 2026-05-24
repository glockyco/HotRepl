using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Internal;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Jobs;

internal sealed class ControlJobManager
{
    private readonly int _maxEventBuffer;
    private readonly int _maxRunningJobs;
    private readonly object _sync = new();
    private readonly Dictionary<string, JobState> _jobs = new(StringComparer.Ordinal);

    public ControlJobManager(int maxEventBuffer, int maxRunningJobs = int.MaxValue)
    {
        _maxEventBuffer = Math.Max(1, maxEventBuffer);
        _maxRunningJobs = Math.Max(1, maxRunningJobs);
    }

    public ControlJob StartJob(
        string requestId,
        Func<JobExecutionEnvironment, CancellationToken, ValueTask<CompiledCommandResult>> execute
    ) => StartJob(Guid.Empty, requestId, execute);

    public ControlJob StartJob(
        Guid connectionId,
        string requestId,
        Func<JobExecutionEnvironment, CancellationToken, ValueTask<CompiledCommandResult>> execute
    )
    {
        if (execute == null)
        {
            throw new ArgumentNullException(nameof(execute));
        }

        var jobId = Guid.NewGuid().ToString("N");
        var state = new JobState(jobId, connectionId, requestId, execute);
        lock (_sync)
        {
            if (RunningJobCountLocked() >= _maxRunningJobs)
            {
                throw new InvalidOperationException("maxJobConcurrency");
            }

            _jobs.Add(jobId, state);
            AddEventLocked(state, ControlJobStates.Running, progress: null, message: null);
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
            {
                return;
            }
        }

        try
        {
            var env = new JobExecutionEnvironment(state.JobId, Report, state.Artifacts);
            var result = await state.Execute(env, state.Cancellation.Token).ConfigureAwait(false);
            CompleteJobLocked(state, result);
        }
        catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested)
        {
            lock (_sync)
            {
                TransitionLocked(state, ControlJobStates.Cancelled, message: null);
            }
        }
        catch (Exception ex)
        {
            FailJobWithException(state, ex);
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

    private void CompleteJobLocked(JobState state, CompiledCommandResult result)
    {
        lock (_sync)
        {
            state.Result = result.Output;
            state.ArtifactList = result.Artifacts.ToArray();
            state.Diagnostics = result.Diagnostics.ToArray();
            if (!result.Succeeded)
            {
                state.Error = result.Diagnostics.Count > 0 ? result.Diagnostics[0] : null;
                TransitionLocked(state, ControlJobStates.Failed, message: state.Error?.Message);
            }
            else
            {
                TransitionLocked(state, ControlJobStates.Completed, message: null);
            }
        }
    }

    private void FailJobWithException(JobState state, Exception ex)
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

    public bool Cancel(string jobId)
    {
        lock (_sync)
        {
            var state = RequireJob(jobId);
            if (IsTerminal(state.State))
            {
                return false;
            }

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
                state.ArtifactList,
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

    public bool IsOwnedByConnection(string jobId, Guid connectionId)
    {
        lock (_sync)
        {
            var state = RequireJob(jobId);
            return state.ConnectionId == connectionId;
        }
    }

    private int RunningJobCountLocked() => _jobs.Values.Count(state => !IsTerminal(state.State));

    private static ControlJob Snapshot(JobState state) =>
        new(state.JobId, state.RequestId, state.State);

    private JobState RequireJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            throw new KeyNotFoundException($"Unknown control job '{jobId}'.");
        }

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
        {
            state.Events.Dequeue();
        }
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
            Guid connectionId,
            string requestId,
            Func<
                JobExecutionEnvironment,
                CancellationToken,
                ValueTask<CompiledCommandResult>
            > execute
        )
        {
            JobId = jobId;
            ConnectionId = connectionId;
            RequestId = requestId;
            Execute = execute;
            Artifacts = new InMemoryArtifactWriter();
        }

        public string JobId { get; }
        public Guid ConnectionId { get; }
        public string RequestId { get; }
        public Func<
            JobExecutionEnvironment,
            CancellationToken,
            ValueTask<CompiledCommandResult>
        > Execute { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public string State { get; set; } = ControlJobStates.Running;
        public long NextSequence { get; set; }
        public JObject? Progress { get; set; }
        public JObject? Result { get; set; }
        public InMemoryArtifactWriter Artifacts { get; }
        public ArtifactRef[] ArtifactList { get; set; } = Array.Empty<ArtifactRef>();
        public ControlCommandError[] Diagnostics { get; set; } = Array.Empty<ControlCommandError>();
        public ControlCommandError? Error { get; set; }
        public Queue<ControlJobEvent> Events { get; } = new();
    }
}
