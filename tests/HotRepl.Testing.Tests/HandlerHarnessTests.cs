using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
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
}
