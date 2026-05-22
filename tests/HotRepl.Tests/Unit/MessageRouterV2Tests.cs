using System;
using System.Collections.Generic;
using HotRepl.Engine.Commands;
using HotRepl.Evaluator;
using HotRepl.Server;
using Xunit;

namespace HotRepl.Tests.Unit;

public class MessageRouterV2Tests
{
    [Fact]
    public void Router_RejectsLegacyControlAuthMessage()
    {
        var sent = new List<string>();
        var router = MessageRouter.CreateForTests(
            _ => throw new InvalidOperationException("Legacy message must not enqueue."),
            _ => throw new InvalidOperationException("Legacy message must not enqueue eval."),
            _ => throw new InvalidOperationException("Legacy message must not cancel."),
            (_, json) => sent.Add(json),
            new ReplConfig(),
            () => 0
        );

        router.HandleMessage(Guid.NewGuid(), "{\"type\":\"control_auth\",\"id\":\"legacy\"}");

        var json = Assert.Single(sent);
        Assert.Contains("\"kind\":\"invalid_request\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"legacyMessageType\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Router_RejectsLeaseAcquireMessage()
    {
        var sent = new List<string>();
        var router = MessageRouter.CreateForTests(
            _ => throw new InvalidOperationException("Legacy message must not enqueue."),
            _ => throw new InvalidOperationException("Legacy message must not enqueue eval."),
            _ => throw new InvalidOperationException("Legacy message must not cancel."),
            (_, json) => sent.Add(json),
            new ReplConfig(),
            () => 0
        );

        router.HandleMessage(Guid.NewGuid(), "{\"type\":\"lease_acquire\",\"id\":\"legacy\"}");

        var json = Assert.Single(sent);
        Assert.Contains("\"kind\":\"invalid_request\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"legacyMessageType\"", json, StringComparison.Ordinal);
    }
}
