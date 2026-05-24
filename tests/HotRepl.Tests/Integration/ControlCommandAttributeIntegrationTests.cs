using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Integration;

public sealed class ControlCommandAttributeIntegrationTests
{
    private sealed class Args
    {
        public string Name { get; set; } = "";
    }

    private sealed class Output
    {
        public string Reply { get; set; } = "";
    }

    [ControlCommand(
        "example.attr",
        Version = 7,
        Kind = ControlCommandKind.Job,
        MutatesState = true
    )]
    private sealed class AttrHandler : IControlCommandHandler<Args, Output>
    {
        public string Name => "wrong.name";

        public int Version => 1;

        public ControlCommandKind Kind => ControlCommandKind.Sync;

        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<Output>> ExecuteAsync(
            ControlCommandContext<Output> context,
            Args args,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(context.Ok(new Output { Reply = $"hi {args.Name}" }));
    }

    private sealed class PropertyHandler : IControlCommandHandler<Args, Output>
    {
        public string Name => "example.props";

        public int Version => 3;

        public ControlCommandKind Kind => ControlCommandKind.Sync;

        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<Output>> ExecuteAsync(
            ControlCommandContext<Output> context,
            Args args,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(context.Ok(new Output { Reply = $"hi {args.Name}" }));
    }

    [Fact]
    public void Descriptor_TakesAttributeOverHandlerProperties()
    {
        var registry = new GlobalControlCommandRegistry();
        registry.Register(new AttrHandler());

        var descriptor = registry
            .Describe()
            .Single(x => string.Equals(x.Name, "example.attr", System.StringComparison.Ordinal));

        Assert.Equal("example.attr", descriptor.Name);
        Assert.Equal(7, descriptor.Version);
        Assert.Equal(ControlCommandKind.Job, descriptor.Kind);
        Assert.True(descriptor.MutatesState);
    }

    [Fact]
    public void Descriptor_FallsBackToHandlerPropertiesWhenAttributeAbsent()
    {
        var registry = new GlobalControlCommandRegistry();
        registry.Register(new PropertyHandler());

        var descriptor = registry
            .Describe()
            .Single(x => string.Equals(x.Name, "example.props", System.StringComparison.Ordinal));

        Assert.Equal("example.props", descriptor.Name);
        Assert.Equal(3, descriptor.Version);
        Assert.Equal(ControlCommandKind.Sync, descriptor.Kind);
        Assert.False(descriptor.MutatesState);
    }
}
