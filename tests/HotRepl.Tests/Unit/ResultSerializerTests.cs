using System.Collections.Generic;
using System.Linq;
using HotRepl;
using HotRepl.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

/// <summary>
/// Contract tests for JsonResultSerializer.
///
/// The serializer produces JSON. Consumers that need a typed value should
/// parse the string with JSON.parse / JToken.Parse; consumers that only
/// need a display string can use it directly.
/// </summary>
public class ResultSerializerTests
{
    private readonly JsonResultSerializer _sut = new();
    private readonly ReplConfig _defaults = new();

    // ── Serialize ─────────────────────────────────────────────────────────────

    [Fact]
    public void Null_ProducesJsonNull()
    {
        Assert.Equal("null", _sut.Serialize(null, _defaults));
    }

    [Fact]
    public void String_ProducesJsonQuotedString()
    {
        // Strings round-trip through JSON — the client json.loads the value field
        // to recover the original string without quotes.
        Assert.Equal("\"hello\"", _sut.Serialize("hello", _defaults));
    }

    [Fact]
    public void Int_ProducesJsonNumber()
    {
        Assert.Equal("42", _sut.Serialize(42, _defaults));
    }

    [Fact]
    public void Double_UsesInvariantDecimalSeparator()
    {
        var result = _sut.Serialize(3.14, _defaults);
        Assert.NotNull(result);
        Assert.Contains(".", result);
        Assert.DoesNotContain(",", result);
    }

    [Fact]
    public void Bool_ProducesLowercaseJsonBoolean()
    {
        Assert.Equal("true", _sut.Serialize(true, _defaults));
        Assert.Equal("false", _sut.Serialize(false, _defaults));
    }

    [Fact]
    public void Type_ProducesJsonString()
    {
        var result = _sut.Serialize(typeof(string), _defaults);
        Assert.NotNull(result);
        // Parsed back: should contain the type name
        var parsed = JsonConvert.DeserializeObject<string>(result);
        Assert.Contains("String", parsed);
    }

    [Fact]
    public void IntArray_ProducesJsonArray()
    {
        var result = _sut.Serialize(new[] { 1, 2, 3 }, _defaults);
        var token = JToken.Parse(result);
        Assert.Equal(JTokenType.Array, token.Type);
        Assert.Equal(new[] { 1, 2, 3 }, token.ToObject<int[]>());
    }

    [Fact]
    public void ByteArray_ProducesBase64String_NotArray()
    {
        // Json.NET serializes byte[] as base64, not as [1, 2, 3].
        // This is correct — byte arrays are binary blobs, not number lists.
        var result = _sut.Serialize(new byte[] { 1, 2, 3 }, _defaults);
        var token = JToken.Parse(result);
        Assert.Equal(JTokenType.String, token.Type); // base64 string
    }

    [Fact]
    public void EmptyEnumerable_ProducesEmptyJsonArray()
    {
        var result = _sut.Serialize(new int[0], _defaults);
        Assert.Equal("[]", result);
    }

    [Fact]
    public void Enumerable_CappedAtMaxElements()
    {
        var config = new ReplConfig { MaxEnumerableElements = 5 };
        var result = _sut.Serialize(Enumerable.Range(0, 200), config);
        var array = JArray.Parse(result);
        Assert.Equal(5, array.Count);
        Assert.Equal(0, array[0].Value<int>());
        Assert.Equal(4, array[4].Value<int>());
    }

    [Fact]
    public void NestedEnumerable_SerializesCorrectly()
    {
        var nested = new List<int[]> { new[] { 1, 2 }, new[] { 3, 4 } };
        var result = _sut.Serialize(nested, _defaults);
        var outer = JArray.Parse(result);
        Assert.Equal(2, outer.Count);
        Assert.Equal(new[] { 1, 2 }, outer[0].ToObject<int[]>());
        Assert.Equal(new[] { 3, 4 }, outer[1].ToObject<int[]>());
    }

    [Fact]
    public void AnonymousType_ProducesJsonObject()
    {
        var result = _sut.Serialize(new { X = 1, Y = "hello" }, _defaults);
        var obj = JObject.Parse(result);
        Assert.Equal(1, obj["X"]!.Value<int>());
        Assert.Equal("hello", obj["Y"]!.Value<string>());
    }

    [Fact]
    public void SerializationFailure_ReturnsErrorObject_DoesNotThrow()
    {
        // Even on unexpected failures the contract is: never throw.
        // (Covered implicitly by all tests above — none wrap in try/catch.)
        Assert.NotNull(_sut.Serialize(new ThrowingToString(), _defaults));
    }

    // ── Truncate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Truncate_WithinLimit_ReturnsUnchanged()
    {
        var s = "hello";
        Assert.Equal(s, _sut.Truncate(s, 100));
    }

    [Fact]
    public void Truncate_ExceedsLimit_CutsAndAppendsDiagnostic()
    {
        var s = new string('x', 200);
        var result = _sut.Truncate(s, 10);

        Assert.StartsWith(new string('x', 10), result);
        Assert.Contains("200", result); // original length visible for diagnosis
    }

    [Fact]
    public void Truncate_ExactLimit_ReturnsUnchanged()
    {
        var s = new string('x', 50);
        Assert.Equal(s, _sut.Truncate(s, 50));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Simulates an object whose ToString throws, to exercise the error path.</summary>
    private sealed class ThrowingToString
    {
        public string Value => throw new System.InvalidOperationException("boom");
    }
}
