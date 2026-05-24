using System;
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class ControlCommandContextTests
{
    private sealed class Output
    {
        public int Value { get; init; }
    }

    [Fact]
    public void Ok_BuildsSuccessfulResult()
    {
        var ctx = TestContext.Create<Output>();
        var result = ctx.Ok(new Output { Value = 42 });

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Output!.Value);
    }

    [Fact]
    public void PreconditionFailed_BuildsFailureResult()
    {
        var ctx = TestContext.Create<Output>();
        var result = ctx.PreconditionFailed("missingArg", "x is required");

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("missingArg", diagnostic.Code);
        Assert.Equal(ControlCommandDiagnosticKind.PreconditionFailed, diagnostic.Kind);
    }

    [Fact]
    public void ValidationFailed_BuildsFailureResult()
    {
        var ctx = TestContext.Create<Output>();
        var result = ctx.ValidationFailed("badShape", "args missing required property");

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ControlCommandDiagnosticKind.ValidationFailed, diagnostic.Kind);
    }

    private static class TestContext
    {
        public static ControlCommandContext<T> Create<T>(IArtifactWriter? artifacts = null) =>
            new(
                "req-1",
                TimeSpan.FromSeconds(30),
                jobId: null,
                progress: new Progress<ControlCommandProgress>(),
                artifacts: artifacts ?? new InMemoryArtifactWriter()
            );
    }
}
