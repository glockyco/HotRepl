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

    [Fact]
    public void Router_RoutesCommandsListMessage()
    {
        var queued = new List<IEngineCommand>();
        var router = MessageRouter.CreateForTests(
            queued.Add,
            _ => throw new InvalidOperationException("commands_list must not enqueue eval."),
            _ => throw new InvalidOperationException("commands_list must not cancel."),
            (_, _) => throw new InvalidOperationException("commands_list must not send error."),
            new ReplConfig(),
            () => 0
        );

        router.HandleMessage(Guid.NewGuid(), "{\"type\":\"commands_list\",\"id\":\"list-1\"}");

        var command = Assert.IsType<CommandsListCmd>(Assert.Single(queued));
        Assert.Equal("list-1", command.Id);
    }

    [Fact]
    public void Router_RoutesJournalQueryMessage()
    {
        var queued = new List<IEngineCommand>();
        var router = MessageRouter.CreateForTests(
            queued.Add,
            _ => throw new InvalidOperationException("journal_query must not enqueue eval."),
            _ => throw new InvalidOperationException("journal_query must not cancel."),
            (_, _) => throw new InvalidOperationException("journal_query must not send error."),
            new ReplConfig(),
            () => 0
        );

        router.HandleMessage(
            Guid.NewGuid(),
            "{\"type\":\"journal_query\",\"id\":\"journal-1\",\"kind\":\"eval\",\"limit\":5}"
        );

        var command = Assert.IsType<JournalQueryCmd>(Assert.Single(queued));
        Assert.Equal("journal-1", command.Id);
        Assert.Equal("eval", command.Kind);
        Assert.Equal(5, command.Limit);
    }
}
