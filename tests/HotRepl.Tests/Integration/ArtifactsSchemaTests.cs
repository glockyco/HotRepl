using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Integration;

public sealed class ArtifactsSchemaTests
{
    [Fact]
    public void Descriptor_ArtifactsSchema_ReflectsDeclaredAttributes()
    {
        var registry = new GlobalControlCommandRegistry();
        registry.Register(new TwoArtifactHandler());

        var descriptor = registry
            .Describe()
            .Single(x => string.Equals(x.Name, "test.two-artifacts", StringComparison.Ordinal));
        var schema = descriptor.ArtifactsSchema;

        Assert.Equal("object", schema["type"]!.ToString());
        var patterns = Assert.IsType<JObject>(schema["patternProperties"]);
        Assert.Equal(2, patterns.Count);
        Assert.Contains("^data\\.[^.]+$", patterns.Properties().Select(p => p.Name));
        Assert.Contains("^screenshots\\.metadata$", patterns.Properties().Select(p => p.Name));

        var required = Assert.IsType<JArray>(schema["required"]);
        Assert.Contains("screenshots.metadata", required.Select(x => x.ToString()));
    }

    [ControlCommand("test.two-artifacts")]
    [ControlCommandArtifact(
        "data.<stem>",
        ContentType = "application/json",
        Required = true,
        RepeatCount = "1..*"
    )]
    [ControlCommandArtifact(
        "screenshots.metadata",
        ContentType = "application/json",
        Required = true
    )]
    private sealed class TwoArtifactHandler : IControlCommandHandler<EmptyArgs, object>
    {
        public string Name => "test.two-artifacts";

        public int Version => 1;

        public ControlCommandKind Kind => ControlCommandKind.Sync;

        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<object>> ExecuteAsync(
            ControlCommandContext<object> context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(context.Ok((object)new { }));
    }
}
