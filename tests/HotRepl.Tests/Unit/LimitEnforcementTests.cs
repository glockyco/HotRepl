using System;
using System.Collections.Generic;
using System.Reflection;
using HotRepl.Control;
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
    public void QueuedCommandCount_IncludesQueuedEvalRequests()
    {
        var engine = new ReplEngine(new QueueCountingHost());

        engine.EnqueueEval(new EvalJob("eval-1", "1 + 1", 1000, Guid.NewGuid()));

        Assert.Equal(1, engine.QueuedCommandCount);
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
    private sealed class QueueCountingHost : IReplHost
    {
        public ReplConfig Config { get; } = new();

        public HostInfo HostInfo { get; } =
            new()
            {
                Name = "Tests",
                Version = "1.0.0",
                Runtime = ".NET",
                Platform = "Unit",
            };

        public IControlCommandRegistry ControlCommands => EmptyControlCommandRegistry.Instance;

        public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators =>
            Array.Empty<EvaluatorCapabilities>();

        public string DefaultEvaluatorName => "none";

        public IReadOnlyList<Assembly> AdditionalAssemblies => Array.Empty<Assembly>();

        public IReadOnlyList<string> AdditionalUsings => Array.Empty<string>();

        public string[] AdditionalHelperSignatures => Array.Empty<string>();

        public ICodeEvaluator CreateEvaluator(string evaluatorName) =>
            throw new NotSupportedException();

        public void LogInfo(string message) { }

        public void LogDebug(string message) { }

        public void LogWarning(string message) { }

        public void LogError(string message, Exception? ex = null) { }
    }
}
