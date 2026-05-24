using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Testing.Tests;

public sealed class HandlerHarnessTests
{
    public sealed class Args
    {
        [Required]
        public string Name { get; set; } = "";
    }

    public sealed class Output
    {
        public string Reply { get; set; } = "";
    }

    [ControlCommand("test.echo")]
    private sealed class EchoHandler : IControlCommandHandler<Args, Output>
    {
        public string Name => "test.echo";

        public int Version => 1;

        public ControlCommandKind Kind => ControlCommandKind.Sync;

        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<Output>> ExecuteAsync(
            ControlCommandContext<Output> context,
            Args args,
            CancellationToken cancellationToken
        ) =>
            string.IsNullOrEmpty(args.Name)
                ? new ValueTask<ControlCommandResult<Output>>(
                    context.ValidationFailed("nameRequired", "Name is required.")
                )
                : new ValueTask<ControlCommandResult<Output>>(
                    context.Ok(new Output { Reply = $"hi {args.Name}" })
                );
    }

    [Fact]
    public void GenerateSchema_ReturnsArgsSchema()
    {
        var schema = HandlerHarness.GenerateSchema<Args>();

        Assert.Contains("Name", schema.ToString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_FailsOnMissingRequiredProperty()
    {
        var result = HandlerHarness.Validate<Args>("{}");

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task RunAsync_ReturnsOkResult()
    {
        var result = await HandlerHarness.RunAsync(new EchoHandler(), new Args { Name = "Joe" });

        Assert.True(result.Succeeded);
        Assert.Equal("hi Joe", result.Output!.Reply);
    }

    [Fact]
    public async Task RunAsync_PropagatesValidationFailure()
    {
        var result = await HandlerHarness.RunAsync(new EchoHandler(), new Args { Name = "" });

        Assert.False(result.Succeeded);
        Assert.Equal("nameRequired", result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task ConformanceSuite_RunAllAsync_ChecksCatalogAndDescriptors()
    {
        await using var channel = new FakeFrameChannel();
        await using var session = new HotRepl.Sdk.HotReplSession(
            new HotRepl.Sdk.Internal.MessageDispatcher(channel),
            new HotRepl.Sdk.HotReplCapabilities(new JObject(), 2, schemaValidation: true),
            new HotRepl.Sdk.HotReplClientOptions()
        );

        var pending = ConformanceSuite.RunAllAsync(session);
        await channel.WaitForSentCountAsync(1);
        channel.EnqueueIncoming(
            "{\"type\":\"commands_list_result\",\"id\":\"list-1\",\"commands\":[{\"name\":\"test.echo\",\"majorVersion\":1,\"kind\":\"sync\",\"mutatesState\":false}]}"
        );
        await channel.WaitForSentCountAsync(2);
        channel.EnqueueIncoming(
            "{\"type\":\"command_describe_result\",\"id\":\"describe-2\",\"descriptor\":{\"name\":\"test.echo\",\"majorVersion\":1,\"kind\":\"sync\",\"mutatesState\":false,\"inputSchema\":{},\"outputSchema\":{},\"artifactsSchema\":{}}}"
        );

        var result = await pending;

        Assert.True(result.Passed);
        Assert.All(result.Checks, check => Assert.True(check.Passed));
    }
}
