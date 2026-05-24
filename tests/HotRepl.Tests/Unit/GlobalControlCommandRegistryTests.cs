using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Internal;
using Xunit;

namespace HotRepl.Tests.Unit;

public class GlobalControlCommandRegistryTests
{
    [Fact]
    public void Describe_ReturnsRegisteredCommandsInNameOrder()
    {
        var registry = new GlobalControlCommandRegistry();
        using var z = registry.Register(new Handler("z.command"));
        using var a = registry.Register(new Handler("a.command"));

        var descriptors = registry.Describe();

        Assert.Collection(
            descriptors,
            descriptor => Assert.Equal("a.command", descriptor.Name),
            descriptor => Assert.Equal("z.command", descriptor.Name)
        );
    }

    [Fact]
    public void DisposeRegistration_RemovesHandler()
    {
        var registry = new GlobalControlCommandRegistry();
        var registration = registry.Register(new Handler("archive.info"));
        registration.Dispose();

        Assert.False(((ICompiledRegistry)registry).TryGet("archive.info", out _));
    }

    [Fact]
    public void Register_DuplicateNameThrows()
    {
        var registry = new GlobalControlCommandRegistry();
        using var first = registry.Register(new Handler("archive.info"));
        Assert.Throws<InvalidOperationException>(() =>
        {
            registry.Register(new Handler("archive.info"));
        });
    }

    private sealed class Handler : IControlCommandHandler<EmptyArgs, EmptyArgs>
    {
        public Handler(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EmptyArgs>> ExecuteAsync(
            ControlCommandContext context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult.Ok(new EmptyArgs()));
    }
}
