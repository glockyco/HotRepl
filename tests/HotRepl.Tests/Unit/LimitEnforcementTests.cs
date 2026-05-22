using System;
using System.Collections.Generic;
using HotRepl.Engine.Commands;
using HotRepl.Evaluator;
using HotRepl.Server;
using Xunit;

namespace HotRepl.Tests.Unit;

public class LimitEnforcementTests
{
    [Fact]
    public void QueueLimit_RejectedRequestReturnsBusyWithoutEnqueueing()
    {
        var sent = new List<string>();
        var enqueued = 0;
        var router = MessageRouter.CreateForTests(
            _ => enqueued++,
            _ => throw new InvalidOperationException("Command message must not enqueue eval."),
            _ => throw new InvalidOperationException("Command message must not cancel."),
            (_, json) => sent.Add(json),
            new ReplConfig { MaxQueuedCommands = 1 },
            () => 1
        );

        router.HandleMessage(
            Guid.NewGuid(),
            "{\"type\":\"command_describe\",\"id\":\"describe-1\",\"name\":\"archive.echo\"}"
        );

        Assert.Equal(0, enqueued);
        var json = Assert.Single(sent);
        Assert.Contains("\"kind\":\"busy\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"commandQueueFull\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageSizeLimit_RejectedRequestReturnsInvalidRequestBeforeParsing()
    {
        var sent = new List<string>();
        var router = MessageRouter.CreateForTests(
            _ => throw new InvalidOperationException("Oversized message must not enqueue."),
            _ => throw new InvalidOperationException("Oversized message must not enqueue eval."),
            _ => throw new InvalidOperationException("Oversized message must not cancel."),
            (_, json) => sent.Add(json),
            new ReplConfig { MaxMessageBytes = 10 },
            () => 0
        );

        router.HandleMessage(Guid.NewGuid(), "{\"type\":\"eval\",\"id\":\"eval-1\",\"code\":\"1+1\"}");

        var json = Assert.Single(sent);
        Assert.Contains("\"kind\":\"invalid_request\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"messageTooLarge\"", json, StringComparison.Ordinal);
    }
}
