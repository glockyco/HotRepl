using System;
using HotRepl.Evaluator;
using Xunit;

namespace HotRepl.Tests.Unit;

public class EvalResultTests
{
    [Fact]
    public void Ok_SetsAllProperties()
    {
        var result = EvalOutcome.Ok(42, "System.Int32", 5);

        Assert.True(result.Success);
        Assert.True(result.HasValue);
        Assert.Equal(42, result.Value);
        Assert.Equal("System.Int32", result.ValueType);
        Assert.Equal(5, result.DurationMs);
    }

    [Fact]
    public void Ok_NullValue_HasValueFalse()
    {
        var result = EvalOutcome.Ok(null, null, 1);

        Assert.True(result.Success);
        Assert.False(result.HasValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void OkVoid_NoValue()
    {
        var result = EvalOutcome.OkVoid(3);

        Assert.True(result.Success);
        Assert.False(result.HasValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void CompileError_SetsErrorKind()
    {
        var result = EvalOutcome.CompileError("CS0001: syntax error", 2);

        Assert.False(result.Success);
        Assert.Equal("validation_failed", result.ErrorKind);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("CS0001", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeError_IncludesStackTrace()
    {
        var result = EvalOutcome.RuntimeError("NullRef", "at Foo.Bar()", 10);

        Assert.False(result.Success);
        Assert.Equal("internal", result.ErrorKind);
        Assert.NotNull(result.StackTrace);
        Assert.Equal("at Foo.Bar()", result.StackTrace);
    }

    [Fact]
    public void Timeout_SetsKindAndMessage()
    {
        var result = EvalOutcome.Timeout(10_000);

        Assert.False(result.Success);
        Assert.Equal("timeout", result.ErrorKind);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelled_SetsKindAndMessage()
    {
        var result = EvalOutcome.Cancelled(500);

        Assert.False(result.Success);
        Assert.Equal("cancelled", result.ErrorKind);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.Ordinal);
    }
}
