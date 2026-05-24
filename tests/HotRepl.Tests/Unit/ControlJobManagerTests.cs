using System;
using System.Linq;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Internal;
using HotRepl.Control.Jobs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlJobManagerTests
{
    [Fact]
    public void StartJob_CreatesRunningState()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);

        var job = manager.StartJob(
            "request-1",
            (_, _) => ValueTask.FromResult(CompiledCommandResult.Empty)
        );

        Assert.Equal("running", job.State);
        Assert.Equal(job.JobId, manager.GetStatus(job.JobId).JobId);
        Assert.Equal("running", manager.GetStatus(job.JobId).State);
    }

    [Fact]
    public void StartJob_RejectsWhenRunningJobConcurrencyIsExhausted()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100, maxRunningJobs: 1);
        manager.StartJob("request-1", (_, _) => ValueTask.FromResult(CompiledCommandResult.Empty));

        var error = Assert.Throws<InvalidOperationException>(() =>
            manager.StartJob(
                "request-2",
                (_, _) => ValueTask.FromResult(CompiledCommandResult.Empty)
            )
        );

        Assert.Equal("maxJobConcurrency", error.Message);
    }

    [Fact]
    public async Task RunJob_TransitionsToCompletedWithResult()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var job = manager.StartJob(
            "request-1",
            (_, _) =>
                ValueTask.FromResult(
                    new CompiledCommandResult(
                        Succeeded: true,
                        Output: new JObject { ["ok"] = true },
                        Artifacts: Array.Empty<HotRepl.Control.Artifacts.ArtifactRef>(),
                        Diagnostics: Array.Empty<ControlCommandError>()
                    )
                )
        );

        await manager.RunAsync(job.JobId);

        var status = manager.GetStatus(job.JobId);
        Assert.Equal("done", status.State);
        Assert.True(status.Result!["ok"]!.Value<bool>());
    }

    [Fact]
    public async Task RunJob_HandlerExceptionTransitionsToFailedError()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var job = manager.StartJob(
            "request-1",
            (_, _) => throw new InvalidOperationException("boom")
        );

        await manager.RunAsync(job.JobId);

        var status = manager.GetStatus(job.JobId);
        Assert.Equal("failed", status.State);
        Assert.Equal("internal", status.Error!.Kind);
        Assert.Equal("handlerException", status.Error.Code);
    }

    [Fact]
    public async Task CancelJob_TransitionsToCancelled()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = manager.StartJob(
            "request-1",
            async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return CompiledCommandResult.Empty;
            }
        );

        var run = manager.RunAsync(job.JobId).AsTask();
        await started.Task;

        var accepted = manager.Cancel(job.JobId);
        await run;

        Assert.True(accepted);
        var status = manager.GetStatus(job.JobId);
        Assert.Equal("cancelled", status.State);

        Assert.Contains(
            manager.EventsAfter(job.JobId, 0),
            e => string.Equals(e.State, "cancelled", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EventsAfter_ReturnsEventsAfterRequestedSequence()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var job = manager.StartJob(
            "request-1",
            (env, _) =>
            {
                env.ProgressSink(new JObject { ["step"] = 1 }, "step 1");
                env.ProgressSink(new JObject { ["step"] = 2 }, "step 2");
                return ValueTask.FromResult(CompiledCommandResult.Empty);
            }
        );
        await manager.RunAsync(job.JobId);
        var firstProgress = manager
            .EventsAfter(job.JobId, 0)
            .Single(e => string.Equals(e.Message, "step 1", StringComparison.Ordinal));

        var later = manager.EventsAfter(job.JobId, firstProgress.Sequence);

        Assert.DoesNotContain(
            later,
            e => string.Equals(e.Message, "step 1", StringComparison.Ordinal)
        );
        Assert.Contains(later, e => string.Equals(e.Message, "step 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EventsAfter_CapsAtConfiguredBufferSize()
    {
        var manager = new ControlJobManager(maxEventBuffer: 3);
        var job = manager.StartJob(
            "request-1",
            (env, _) =>
            {
                for (var i = 0; i < 5; i++)
                {
                    env.ProgressSink(new JObject { ["step"] = i }, $"step {i}");
                }

                return ValueTask.FromResult(CompiledCommandResult.Empty);
            }
        );

        await manager.RunAsync(job.JobId);

        var events = manager.EventsAfter(job.JobId, 0);
        Assert.Equal(3, events.Count);
        Assert.Equal("done", events[^1].State);
    }
}
