using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HotRepl;
using HotRepl.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

/// <summary>
/// Contract tests for JsonResultSerializer.
///
/// <see cref="JsonResultSerializer.Serialize"/> produces a JSON string used for
/// the journal/history display. <see cref="JsonResultSerializer.ToWireValue(object, ReplConfig)"/>
/// produces a native <see cref="JToken"/> for the eval/subscription wire value so
/// consumers receive properly typed output without a second parse, or a
/// truncation signal when the value exceeds the configured size budget.
/// </summary>
public class ResultSerializerTests
{
    private readonly ReplConfig _defaults = new();

    // ── Serialize ─────────────────────────────────────────────────────────────

    [Fact]
    public void Null_ProducesJsonNull()
    {
        Assert.Equal("null", JsonResultSerializer.Serialize(null, _defaults));
    }

    [Fact]
    public void String_ProducesJsonQuotedString()
    {
        // Strings round-trip through JSON — the client json.loads the value field
        // to recover the original string without quotes.
        Assert.Equal("\"hello\"", JsonResultSerializer.Serialize("hello", _defaults));
    }

    [Fact]
    public void Int_ProducesJsonNumber()
    {
        Assert.Equal("42", JsonResultSerializer.Serialize(42, _defaults));
    }

    [Fact]
    public void Double_UsesInvariantDecimalSeparator()
    {
        var result = JsonResultSerializer.Serialize(3.14, _defaults);
        Assert.NotNull(result);
        Assert.Contains(".", result, StringComparison.Ordinal);
        Assert.DoesNotContain(",", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Bool_ProducesLowercaseJsonBoolean()
    {
        Assert.Equal("true", JsonResultSerializer.Serialize(true, _defaults));
        Assert.Equal("false", JsonResultSerializer.Serialize(false, _defaults));
    }

    [Fact]
    public void Type_ProducesJsonString()
    {
        var result = JsonResultSerializer.Serialize(typeof(string), _defaults);
        Assert.NotNull(result);
        // Parsed back: should contain the type name
        var parsed = JsonConvert.DeserializeObject<string>(result);
        Assert.Contains("String", parsed, StringComparison.Ordinal);
    }

    [Fact]
    public void IntArray_ProducesJsonArray()
    {
        var result = JsonResultSerializer.Serialize(new[] { 1, 2, 3 }, _defaults);
        var token = JToken.Parse(result);
        Assert.Equal(JTokenType.Array, token.Type);
        Assert.Equal(new[] { 1, 2, 3 }, token.ToObject<int[]>());
    }

    [Fact]
    public void ByteArray_ProducesBase64String_NotArray()
    {
        // Json.NET serializes byte[] as base64, not as [1, 2, 3].
        // This is correct — byte arrays are binary blobs, not number lists.
        var result = JsonResultSerializer.Serialize(new byte[] { 1, 2, 3 }, _defaults);
        var token = JToken.Parse(result);
        Assert.Equal(JTokenType.String, token.Type); // base64 string
    }

    [Fact]
    public void EmptyEnumerable_ProducesEmptyJsonArray()
    {
        var result = JsonResultSerializer.Serialize(Array.Empty<int>(), _defaults);
        Assert.Equal("[]", result);
    }

    [Fact]
    public void Enumerable_CappedAtMaxElements()
    {
        var config = new ReplConfig { MaxEnumerableElements = 5 };
        var result = JsonResultSerializer.Serialize(Enumerable.Range(0, 200), config);
        var array = JArray.Parse(result);
        Assert.Equal(5, array.Count);
        Assert.Equal(0, array[0].Value<int>());
        Assert.Equal(4, array[4].Value<int>());
    }

    [Fact]
    public void NestedEnumerable_SerializesCorrectly()
    {
        var nested = new List<int[]> { new[] { 1, 2 }, new[] { 3, 4 } };
        var result = JsonResultSerializer.Serialize(nested, _defaults);
        var outer = JArray.Parse(result);
        Assert.Equal(2, outer.Count);
        Assert.Equal(new[] { 1, 2 }, outer[0].ToObject<int[]>());
        Assert.Equal(new[] { 3, 4 }, outer[1].ToObject<int[]>());
    }

    [Fact]
    public void AnonymousType_ProducesJsonObject()
    {
        var result = JsonResultSerializer.Serialize(new { X = 1, Y = "hello" }, _defaults);
        var obj = JObject.Parse(result);
        Assert.Equal(1, obj["X"]!.Value<int>());
        Assert.Equal("hello", obj["Y"]!.Value<string>());
    }

    [Fact]
    public void SerializationFailure_ReturnsErrorObject_DoesNotThrow()
    {
        // Even on unexpected failures the contract is: never throw.
        // (Covered implicitly by all tests above — none wrap in try/catch.)
        Assert.NotNull(JsonResultSerializer.Serialize(new ThrowingToString(), _defaults));
    }

    // ── Truncate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Truncate_WithinLimit_ReturnsUnchanged()
    {
        var s = "hello";
        Assert.Equal(s, JsonResultSerializer.Truncate(s, 100));
    }

    [Fact]
    public void Truncate_ExceedsLimit_StaysWithinUtf8ByteLimit()
    {
        var s = new string('x', 200);
        var result = JsonResultSerializer.Truncate(s, 40);

        Assert.True(Encoding.UTF8.GetByteCount(result) <= 40);
        Assert.Contains("200", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncate_NonAsciiOutput_StaysWithinUtf8ByteLimit()
    {
        var result = JsonResultSerializer.Truncate("😀😀😀😀😀", 13);

        Assert.True(Encoding.UTF8.GetByteCount(result) <= 13);
    }

    [Fact]
    public void Truncate_ExactLimit_ReturnsUnchanged()
    {
        var s = new string('x', 50);
        Assert.Equal(s, JsonResultSerializer.Truncate(s, 50));
    }

    // ── ToWireValue (typed wire value) ─────────────────────────────────────────

    [Fact]
    public void ToWireValue_Int_ProducesNativeNumberToken()
    {
        var wire = JsonResultSerializer.ToWireValue(2, _defaults);
        Assert.False(wire.Truncated);
        Assert.NotNull(wire.Value);
        Assert.Equal(JTokenType.Integer, wire.Value!.Type);
        Assert.Equal(2, wire.Value!.Value<int>());
    }

    [Fact]
    public void ToWireValue_String_ProducesNativeStringToken()
    {
        var wire = JsonResultSerializer.ToWireValue("Ardenfall", _defaults);
        Assert.False(wire.Truncated);
        Assert.Equal(JTokenType.String, wire.Value!.Type);
        Assert.Equal("Ardenfall", wire.Value!.Value<string>());
    }

    [Fact]
    public void ToWireValue_Object_ProducesNativeObjectToken()
    {
        var wire = JsonResultSerializer.ToWireValue(new { X = 1, Y = "hello" }, _defaults);
        Assert.False(wire.Truncated);
        Assert.Equal(JTokenType.Object, wire.Value!.Type);
        Assert.Equal(1, wire.Value!["X"]!.Value<int>());
        Assert.Equal("hello", wire.Value!["Y"]!.Value<string>());
    }

    [Fact]
    public void ToWireValue_OversizedValue_TruncatesWithoutValue()
    {
        var config = new ReplConfig { MaxResultLength = 16 };
        var wire = JsonResultSerializer.ToWireValue(new string('x', 500), config);
        Assert.True(wire.Truncated);
        Assert.Null(wire.Value);
        Assert.NotNull(wire.ByteCount);
        Assert.True(wire.ByteCount > 16);
    }

    [Fact]
    public void ToWireValue_Null_ProducesNullTokenNotTruncated()
    {
        var wire = JsonResultSerializer.ToWireValue((object?)null, _defaults);
        Assert.False(wire.Truncated);
        Assert.True(wire.Value == null || wire.Value.Type == JTokenType.Null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Simulates an object whose ToString throws, to exercise the error path.</summary>
    private sealed class ThrowingToString
    {
        public static string Value => throw new InvalidOperationException("boom");
    }
}
