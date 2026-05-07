using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlRoutingTests
{
    [Fact]
    public void Describe_ReturnsDescriptorsFromRegistry()
    {
        var router = new ControlCommandRouter(new FakeRegistry(new EchoHandler()));

        var result = router.Describe("describe-1");

        Assert.Equal(MessageType.CommandDescribeResult, result.Type);
        Assert.Equal("describe-1", result.Id);
        var descriptor = Assert.Single(result.Commands);
        Assert.Equal("archive.echo", descriptor.Name);
        Assert.Equal(1, descriptor.Version);
        Assert.Equal("sync", descriptor.Kind);
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsCommandError()
    {
        var router = new ControlCommandRouter(EmptyControlCommandRegistry.Instance);
        var message = new CommandCallMessage { Id = "cmd-1", Name = "missing.command" };

        var result = router.Execute(message);

        var error = Assert.IsType<CommandErrorMessage>(result);
        Assert.Equal(MessageType.CommandError, error.Type);
        Assert.Equal("cmd-1", error.Id);
        Assert.Equal("failed", error.Status);
        Assert.Equal("unknown_command", error.Error.Kind);
        Assert.False(error.Error.Retryable);
    }

    [Fact]
    public void Execute_KnownSynchronousCommand_ReturnsCommandResult()
    {
        var router = new ControlCommandRouter(new FakeRegistry(new EchoHandler()));
        var message = new CommandCallMessage
        {
            Id = "cmd-1",
            Name = "archive.echo",
            Args = JObject.Parse("{\"value\":\"ok\"}"),
            TimeoutMs = 5000,
            IdempotencyKey = "run/echo/1",
        };

        var result = router.Execute(message);

        var ok = Assert.IsType<CommandResultMessage>(result);
        Assert.Equal(MessageType.CommandResult, ok.Type);
        Assert.Equal("cmd-1", ok.Id);
        Assert.Equal("ok", ok.Status);
        Assert.Equal("ok", ok.Result["value"]!.Value<string>());
    }

    [Fact]
    public void Execute_HandlerException_ReturnsInternalCommandError()
    {
        var router = new ControlCommandRouter(new FakeRegistry(new ThrowingHandler()));
        var message = new CommandCallMessage { Id = "cmd-1", Name = "archive.throw" };

        var result = router.Execute(message);

        var error = Assert.IsType<CommandErrorMessage>(result);
        Assert.Equal("failed", error.Status);
        Assert.Equal("internal", error.Error.Kind);
        Assert.Equal("handlerException", error.Error.Code);
        Assert.False(error.Error.Retryable);
        Assert.Contains("boom", error.Error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRegistry : IControlCommandRegistry
    {
        private readonly IControlCommandHandler _handler;

        public FakeRegistry(IControlCommandHandler handler) => _handler = handler;

        public IReadOnlyList<ControlCommandDescriptor> Describe() => new[] { _handler.Descriptor };

        public bool TryGet(string name, out IControlCommandHandler handler)
        {
            handler = _handler;
            return string.Equals(name, _handler.Descriptor.Name, StringComparison.Ordinal);
        }
    }

    private sealed class EchoHandler : IControlCommandHandler
    {
        public ControlCommandDescriptor Descriptor { get; } = new(
            "archive.echo",
            1,
            ControlCommandKind.Synchronous,
            mutatesState: false,
            argsSchema: JObject.Parse("{\"type\":\"object\"}"),
            resultSchema: JObject.Parse("{\"type\":\"object\"}"));

        public ValueTask<ControlCommandResult> ExecuteAsync(
            ControlCommandContext context,
            JObject args,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ControlCommandResult(
                new JObject { ["value"] = args["value"]?.Value<string>() ?? "" },
                Array.Empty<HotRepl.Control.Artifacts.ArtifactRef>(),
                Array.Empty<ControlCommandError>()));
        }
    }

    private sealed class ThrowingHandler : IControlCommandHandler
    {
        public ControlCommandDescriptor Descriptor { get; } = new(
            "archive.throw",
            1,
            ControlCommandKind.Synchronous,
            mutatesState: false,
            argsSchema: JObject.Parse("{\"type\":\"object\"}"),
            resultSchema: JObject.Parse("{\"type\":\"object\"}"));

        public ValueTask<ControlCommandResult> ExecuteAsync(
            ControlCommandContext context,
            JObject args,
            CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }
}
