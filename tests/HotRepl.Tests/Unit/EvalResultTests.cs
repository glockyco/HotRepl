using HotRepl.Evaluator;
using Xunit;

namespace HotRepl.Tests.Unit;

public class EvalResultTests
{
    [Fact]
    public void Ok_SetsAllProperties()
    {
        var result = EvalOutcome.Ok(42, "System.Int32", null, 5);

        Assert.True(result.Success);
        Assert.True(result.HasValue);
        Assert.Equal(42, result.Value);
        Assert.Equal("System.Int32", result.ValueType);
        Assert.Null(result.Stdout);
        Assert.Equal(5, result.DurationMs);
    }

    [Fact]
    public void Ok_NullValue_HasValueFalse()
    {
        var result = EvalOutcome.Ok(null, null, null, 1);

        Assert.True(result.Success);
        Assert.False(result.HasValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void OkVoid_NoValue()
    {
        var result = EvalOutcome.OkVoid(null, 3);

        Assert.True(result.Success);
        Assert.False(result.HasValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void OkVoid_CapturesStdout()
    {
        var result = EvalOutcome.OkVoid("hello", 1);

        Assert.Equal("hello", result.Stdout);
    }

    [Fact]
    public void CompileError_SetsErrorKind()
    {
        var result = EvalOutcome.CompileError("CS0001: syntax error", null, 2);

        Assert.False(result.Success);
        Assert.Equal("compile", result.ErrorKind);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("CS0001", result.ErrorMessage);
    }

    [Fact]
    public void RuntimeError_IncludesStackTrace()
    {
        var result = EvalOutcome.RuntimeError("NullRef", "at Foo.Bar()", null, 10);

        Assert.False(result.Success);
        Assert.Equal("runtime", result.ErrorKind);
        Assert.NotNull(result.StackTrace);
        Assert.Equal("at Foo.Bar()", result.StackTrace);
    }

    [Fact]
    public void Timeout_SetsKindAndMessage()
    {
        var result = EvalOutcome.Timeout(10_000);

        Assert.False(result.Success);
        Assert.Equal("timeout", result.ErrorKind);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public void Cancelled_SetsKindAndMessage()
    {
        var result = EvalOutcome.Cancelled(500);

        Assert.False(result.Success);
        Assert.Equal("cancelled", result.ErrorKind);
        Assert.Contains("cancelled", result.ErrorMessage);
    }
}
