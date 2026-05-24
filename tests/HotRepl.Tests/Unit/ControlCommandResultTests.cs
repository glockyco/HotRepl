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
}
