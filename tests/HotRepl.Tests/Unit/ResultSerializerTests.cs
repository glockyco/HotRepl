using System.Collections.Generic;
using System.Linq;
using HotRepl.Serialization;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ResultSerializerTests
{
    [Fact]
    public void Null_ReturnsNull()
    {
        Assert.Null(ResultSerializer.Serialize(null));
    }

    [Fact]
    public void String_PassesThrough()
    {
        Assert.Equal("hello", ResultSerializer.Serialize("hello"));
    }

    [Fact]
    public void Int_InvariantCulture()
    {
        Assert.Equal("42", ResultSerializer.Serialize(42));
    }

    [Fact]
    public void Double_InvariantCulture()
    {
        var result = ResultSerializer.Serialize(3.14);
        Assert.NotNull(result);
        Assert.Contains(".", result);
        Assert.DoesNotContain(",", result);
    }

    [Fact]
    public void Bool_ReturnsString()
    {
        Assert.Equal("True", ResultSerializer.Serialize(true));
    }

    [Fact]
    public void Decimal_InvariantCulture()
    {
        var result = ResultSerializer.Serialize(1.5m);
        Assert.NotNull(result);
        Assert.Contains(".", result);
    }

    [Fact]
    public void Type_ReturnsToString()
    {
        var result = ResultSerializer.Serialize(typeof(string));
        Assert.NotNull(result);
        Assert.Contains("String", result);
    }

    [Fact]
    public void List_BracketedFormat()
    {
        Assert.Equal("[1, 2, 3]", ResultSerializer.Serialize(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Enumerable_TruncatesAtMaxElements()
    {
        var result = ResultSerializer.Serialize(Enumerable.Range(0, 200), maxElements: 5);
        Assert.NotNull(result);
        Assert.StartsWith("[", result);
        Assert.Contains("...", result);
        // Should have exactly 5 elements before truncation
        Assert.Contains("4", result);
        Assert.DoesNotContain("5,", result);
    }

    [Fact]
    public void EmptyEnumerable_EmptyBrackets()
    {
        Assert.Equal("[]", ResultSerializer.Serialize(new int[0]));
    }

    [Fact]
    public void ByteArray_NotEnumerated()
    {
        var result = ResultSerializer.Serialize(new byte[] { 1, 2, 3 });
        Assert.NotNull(result);
        // byte[] is excluded from enumerable path — should NOT produce "[1, 2, 3]"
        Assert.NotEqual("[1, 2, 3]", result);
    }

    [Fact]
    public void NestedEnumerable_Recurses()
    {
        var nested = new List<int[]> { new[] { 1, 2 }, new[] { 3, 4 } };
        var result = ResultSerializer.Serialize(nested);
        Assert.NotNull(result);
        Assert.Contains("[1, 2]", result);
        Assert.Contains("[3, 4]", result);
    }

    [Fact]
    public void AnonymousType_SerializesAsJson()
    {
        var result = ResultSerializer.Serialize(new { X = 1 });
        Assert.NotNull(result);
        Assert.Contains("X", result);
        Assert.Contains("1", result);
    }
}
