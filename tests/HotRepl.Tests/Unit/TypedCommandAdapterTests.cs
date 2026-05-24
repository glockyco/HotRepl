using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Internal;
using HotRepl.Control.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class TypedCommandAdapterTests
{
    private sealed class GreetArgs
    {
        [Required]
        public string Name { get; set; } = "";
    }

    private sealed class GreetResult
    {
        public string Greeting { get; set; } = "";
    }

    private sealed class GreetCommand : IControlCommandHandler<GreetArgs, GreetResult>
    {
        public string Name => "test.greet";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext context,
            GreetArgs args,
            CancellationToken cancellationToken
        ) =>
            new(
                ControlCommandResult<GreetResult>.Ok(
                    new GreetResult { Greeting = $"Hello, {args.Name}!" }
                )
            );
    }

    private static readonly JsonSerializer Serializer = JsonSerializer.CreateDefault();
    private static readonly NJsonSchemaValidator Validator = new();

    private static CompiledCommandContext NewContext() =>
        new(
            requestId: "req-1",
            timeout: null,
            jobId: null,
            progressSink: null,
            artifacts: new InMemoryArtifactWriter()
        );

    [Fact]
    public async Task ValidArgs_FlowsThroughHandlerAndReturnsOutput()
    {
        var adapter = new TypedCommandAdapter<GreetArgs, GreetResult>(
            new GreetCommand(),
            Serializer,
            Validator
        );

        var result = await adapter.ExecuteAsync(
            NewContext(),
            JObject.Parse("{\"Name\":\"World\"}"),
            CancellationToken.None
        );

        Assert.True(result.Succeeded);
        Assert.Equal("Hello, World!", (string?)result.Output["Greeting"]);
    }

    [Fact]
    public async Task InvalidArgs_FailValidationWithoutCallingHandler()
    {
        var called = false;
        var handler = new TrackingCommand(_ => called = true);
        var adapter = new TypedCommandAdapter<GreetArgs, GreetResult>(
            handler,
            Serializer,
            Validator
        );

        var result = await adapter.ExecuteAsync(
            NewContext(),
            JObject.Parse("{}"),
            CancellationToken.None
        );

        Assert.False(called);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Equal("validation_failed", result.Diagnostics[0].Kind);
    }

    [Fact]
    public async Task HandlerArtifacts_ProjectIntoTopLevelList()
    {
        var adapter = new TypedCommandAdapter<EmptyArgs, GreetResult>(
            new ArtifactProducingCommand(),
            Serializer,
            Validator
        );

        var result = await adapter.ExecuteAsync(
            NewContext(),
            new JObject(),
            CancellationToken.None
        );

        Assert.True(result.Succeeded);
        Assert.Single(result.Artifacts);
        Assert.Equal("manifest", result.Artifacts[0].LogicalName);
    }

    [Fact]
    public async Task PreconditionFailed_BecomesDiagnostic()
    {
        var adapter = new TypedCommandAdapter<EmptyArgs, GreetResult>(
            new FailingCommand(),
            Serializer,
            Validator
        );

        var result = await adapter.ExecuteAsync(
            NewContext(),
            new JObject(),
            CancellationToken.None
        );

        Assert.False(result.Succeeded);
        Assert.Equal("precondition_failed", result.Diagnostics[0].Kind);
    }

    private sealed class TrackingCommand : IControlCommandHandler<GreetArgs, GreetResult>
    {
        private readonly Action<GreetArgs> _onCall;

        public TrackingCommand(Action<GreetArgs> onCall)
        {
            _onCall = onCall;
        }

        public string Name => "test.tracking";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext context,
            GreetArgs args,
            CancellationToken cancellationToken
        )
        {
            _onCall(args);
            return new(ControlCommandResult<GreetResult>.Ok(new GreetResult()));
        }
    }

    private sealed class ArtifactProducingCommand : IControlCommandHandler<EmptyArgs, GreetResult>
    {
        public string Name => "test.artifact";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;

        public async ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext context,
            EmptyArgs args,
            CancellationToken cancellationToken
        )
        {
            var bytes = Encoding.UTF8.GetBytes("manifest content");
            var artifact = await context.Artifacts.WriteAsync(
                "manifest",
                bytes,
                "text/plain",
                cancellationToken
            );
            return ControlCommandResult<GreetResult>.Ok(new GreetResult(), "manifest", artifact);
        }
    }

    private sealed class FailingCommand : IControlCommandHandler<EmptyArgs, GreetResult>
    {
        public string Name => "test.failing";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult<GreetResult>.PreconditionFailed("notReady", "Not ready."));
    }
}
