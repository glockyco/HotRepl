using HotRepl.Evaluation;
using Xunit;

namespace HotRepl.Tests.Unit;

public class EvalResultTests
{
    [Fact]
    public void Ok_SetsAllProperties()
    {
        var result = EvalResult.Ok(42, "System.Int32", null, 5);

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
        var result = EvalResult.Ok(null, null, null, 1);

        Assert.True(result.Success);
        Assert.False(result.HasValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void OkVoid_NoValue()
    {
        var result = EvalResult.OkVoid(null, 3);

        Assert.True(result.Success);
        Assert.False(result.HasValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void OkVoid_CapturesStdout()
    {
        var result = EvalResult.OkVoid("hello", 1);

        Assert.Equal("hello", result.Stdout);
    }

    [Fact]
    public void CompilationError_SetsErrorKind()
    {
        var result = EvalResult.CompilationError("CS0001: syntax error", null, 2);

        Assert.False(result.Success);
        Assert.Equal("compilation", result.ErrorKind);
        Assert.NotNull(result.Error);
        Assert.Contains("CS0001", result.Error);
    }

    [Fact]
    public void RuntimeError_IncludesStackTrace()
    {
        var result = EvalResult.RuntimeError("NullRef", "at Foo.Bar()", null, 10);

        Assert.False(result.Success);
        Assert.Equal("runtime", result.ErrorKind);
        Assert.NotNull(result.StackTrace);
        Assert.Equal("at Foo.Bar()", result.StackTrace);
    }

    [Fact]
    public void Timeout_NotCancelled()
    {
        var result = EvalResult.Timeout(cancelled: false, durationMs: 10000);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Error);
        Assert.Equal("timeout", result.ErrorKind);
    }

    [Fact]
    public void Timeout_Cancelled()
    {
        var result = EvalResult.Timeout(cancelled: true, durationMs: 500);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.Error);
        Assert.Equal("cancelled", result.ErrorKind);
    }
}
