using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using Newtonsoft.Json.Linq;
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

        Assert.False(registry.TryGet("archive.info", out _));
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

    private sealed class Handler : IControlCommandHandler
    {
        public Handler(string name)
        {
            Descriptor = new ControlCommandDescriptor(
                name,
                1,
                ControlCommandKind.Synchronous,
                mutatesState: false,
                argsSchema: JObject.Parse("{\"type\":\"object\"}"),
                resultSchema: JObject.Parse("{\"type\":\"object\"}")
            );
        }

        public ControlCommandDescriptor Descriptor { get; }

        public ValueTask<ControlCommandResult> ExecuteAsync(
            ControlCommandContext context,
            JObject args,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(ControlCommandResult.Empty);
    }
}
