using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Integration;

public sealed class TypedCommandRoundTripTests
{
    [Fact]
    public void Execute_TypedCommand_ReturnsSerializedCommandResult()
    {
        var command = new EchoCommand();
        var registry = new GlobalControlCommandRegistry();
        registry.Register(command);
        var router = new ControlCommandRouter(registry);

        var result = Assert.IsType<CommandResultMessage>(
            router.Execute(
                new CommandCallMessage
                {
                    Id = "req-1",
                    Name = "test.echo",
                    Args = JObject.Parse("{\"Message\":\"hi\"}"),
                }
            )
        );

        Assert.Equal(MessageType.CommandResult, result.Type);
        Assert.Equal("req-1", result.Id);
        Assert.Equal("ok", result.Status);
        Assert.Equal("hi", result.Output!["Echoed"]!.Value<string>());
        Assert.Empty(result.Artifacts);
        Assert.Null(result.Error);
        Assert.Equal(1, command.Calls);
    }

    [Fact]
    public void Execute_InvalidTypedArgs_ReturnsValidationFailureWithoutCallingHandler()
    {
        var command = new EchoCommand();
        var registry = new GlobalControlCommandRegistry();
        registry.Register(command);
        var router = new ControlCommandRouter(registry);

        var result = Assert.IsType<CommandResultMessage>(
            router.Execute(
                new CommandCallMessage
                {
                    Id = "req-2",
                    Name = "test.echo",
                    Args = JObject.Parse("{\"Message\":42}"),
                }
            )
        );

        Assert.Equal(MessageType.CommandResult, result.Type);
        Assert.Equal("req-2", result.Id);
        Assert.Equal("failed", result.Status);
        Assert.Equal(ErrorKind.ValidationFailed, result.Error!.Kind);
        Assert.Equal("argsSchemaViolation", result.Error.Code);
        Assert.Empty(result.Artifacts);
        Assert.Equal(0, command.Calls);
    }

    private sealed class EchoArgs
    {
        [Required]
        public string Message { get; set; } = "";
    }

    private sealed class EchoResult
    {
        public string Echoed { get; set; } = "";
    }

    private sealed class EchoCommand : IControlCommandHandler<EchoArgs, EchoResult>
    {
        public int Calls { get; private set; }

        public string Name => "test.echo";

        public int Version => 1;

        public ControlCommandKind Kind => ControlCommandKind.Synchronous;

        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext context,
            EchoArgs args,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return new(ControlCommandResult.Ok(new EchoResult { Echoed = args.Message }));
        }
    }
}
