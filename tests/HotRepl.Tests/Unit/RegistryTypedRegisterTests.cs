using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Internal;
using Xunit;

namespace HotRepl.Tests.Unit;

public class RegistryTypedRegisterTests
{
    private sealed class Cmd : IControlCommandHandler<EmptyArgs, EmptyArgs>
    {
        public string Name => "test.cmd";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EmptyArgs>> ExecuteAsync(
            ControlCommandContext context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult.Ok(new EmptyArgs()));
    }

    [Fact]
    public void Register_ExposesTypedHandlerThroughDescribe()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new Cmd());

        var descriptors = registry.Describe();
        Assert.Single(descriptors);
        Assert.Equal("test.cmd", descriptors[0].Name);
    }

    [Fact]
    public void Register_DuplicateThrows()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new Cmd());

        Assert.Throws<InvalidOperationException>(() => registry.Register(new Cmd()));
    }

    [Fact]
    public void Dispose_UnregistersHandler()
    {
        var registry = new GlobalControlCommandRegistry();
        var registration = registry.Register(new Cmd());
        registration.Dispose();

        Assert.Empty(registry.Describe());
    }

    [Fact]
    public void TryGet_ReturnsTheCompiledHandler()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new Cmd());

        Assert.True(((ICompiledRegistry)registry).TryGet("test.cmd", out var handler));
        Assert.NotNull(handler);
        Assert.Equal("test.cmd", handler!.Descriptor.Name);
    }
}
