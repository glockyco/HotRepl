using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlCommandResultTests
{
    private sealed class Output
    {
        public int Value { get; init; }
    }

    [Fact]
    public void Ok_SetsSucceededAndOutput()
    {
        var r = ControlCommandResult.Ok(new Output { Value = 42 });
        Assert.True(r.Succeeded);
        Assert.Equal(42, r.Output!.Value);
        Assert.Empty(r.Artifacts);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void ValidationFailed_SetsFailedAndDiagnostic()
    {
        var r = ControlCommandResult.ValidationFailed<Output>("badField", "Field X is required.");
        Assert.False(r.Succeeded);
        Assert.Null(r.Output);
        var d = Assert.Single(r.Diagnostics);
        Assert.Equal(ControlCommandDiagnosticKind.ValidationFailed, d.Kind);
        Assert.Equal("badField", d.Code);
    }

    [Fact]
    public void PreconditionFailed_SetsFailedAndDiagnostic()
    {
        var r = ControlCommandResult.PreconditionFailed<Output>("notReady", "Player not in world.");
        Assert.False(r.Succeeded);
        Assert.Equal(ControlCommandDiagnosticKind.PreconditionFailed, r.Diagnostics[0].Kind);
    }
}
