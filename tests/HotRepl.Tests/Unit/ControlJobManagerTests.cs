using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Jobs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlJobManagerTests
{
    [Fact]
    public void StartJob_CreatesAcceptedState()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);

        var job = manager.StartJob(
            "request-1",
            null,
            null,
            (_, _) => ValueTask.FromResult(ControlCommandResult.Empty)
        );

        Assert.Equal("accepted", job.State);
        Assert.Equal(job.JobId, manager.GetStatus(job.JobId).JobId);
        Assert.Equal("accepted", manager.GetStatus(job.JobId).State);
    }

    [Fact]
    public async Task RunJob_TransitionsToCompletedWithResult()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var job = manager.StartJob(
            "request-1",
            null,
            null,
            (_, _) =>
                ValueTask.FromResult(
                    new ControlCommandResult(
                        new JObject { ["ok"] = true },
                        Array.Empty<HotRepl.Control.Artifacts.ArtifactRef>(),
                        Array.Empty<ControlCommandError>()
                    )
                )
        );

        await manager.RunAsync(job.JobId);

        var status = manager.GetStatus(job.JobId);
        Assert.Equal("completed", status.State);
        Assert.True(status.Result!["ok"]!.Value<bool>());
    }

    [Fact]
    public async Task RunJob_HandlerExceptionTransitionsToFailedError()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var job = manager.StartJob(
            "request-1",
            null,
            null,
            (_, _) => throw new InvalidOperationException("boom")
        );

        await manager.RunAsync(job.JobId);

        var status = manager.GetStatus(job.JobId);
        Assert.Equal("failed", status.State);
        Assert.Equal("internal", status.Error!.Kind);
        Assert.Equal("handlerException", status.Error.Code);
    }

    [Fact]
    public async Task CancelJob_TransitionsThroughCancellingToCancelled()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = manager.StartJob(
            "request-1",
            null,
            null,
            async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return ControlCommandResult.Empty;
            }
        );

        var run = manager.RunAsync(job.JobId).AsTask();
        await started.Task;

        var accepted = manager.Cancel(job.JobId);
        await run;

        Assert.True(accepted);
        var status = manager.GetStatus(job.JobId);
        Assert.Equal("cancelled", status.State);
        Assert.Contains(manager.EventsAfter(job.JobId, 0), e => e.State == "cancelling");
        Assert.Contains(manager.EventsAfter(job.JobId, 0), e => e.State == "cancelled");
    }

    [Fact]
    public async Task EventsAfter_ReturnsEventsAfterRequestedSequence()
    {
        var manager = new ControlJobManager(maxEventBuffer: 100);
        var job = manager.StartJob(
            "request-1",
            null,
            null,
            (context, _) =>
            {
                context.Report(new JObject { ["step"] = 1 }, "step 1");
                context.Report(new JObject { ["step"] = 2 }, "step 2");
                return ValueTask.FromResult(ControlCommandResult.Empty);
            }
        );
        await manager.RunAsync(job.JobId);
        var firstProgress = manager.EventsAfter(job.JobId, 0).Single(e => e.Message == "step 1");

        var later = manager.EventsAfter(job.JobId, firstProgress.Sequence);

        Assert.DoesNotContain(later, e => e.Message == "step 1");
        Assert.Contains(later, e => e.Message == "step 2");
    }

    [Fact]
    public async Task EventsAfter_CapsAtConfiguredBufferSize()
    {
        var manager = new ControlJobManager(maxEventBuffer: 3);
        var job = manager.StartJob(
            "request-1",
            null,
            null,
            (context, _) =>
            {
                for (var i = 0; i < 5; i++)
                    context.Report(new JObject { ["step"] = i }, $"step {i}");
                return ValueTask.FromResult(ControlCommandResult.Empty);
            }
        );

        await manager.RunAsync(job.JobId);

        var events = manager.EventsAfter(job.JobId, 0);
        Assert.Equal(3, events.Count);
        Assert.Equal("completed", events[^1].State);
    }
}
