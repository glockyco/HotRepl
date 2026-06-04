using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Serialization;

/// <summary>
/// Converts arbitrary runtime values to JSON strings using Newtonsoft.Json.
/// Caps IEnumerable values to prevent unbounded output. Truncates the final string
/// to MaxResultLength UTF-8 bytes and appends a diagnostic marker when it fits.
/// Never throws — serialization failures are returned as a JSON error object.
/// </summary>
internal static class JsonResultSerializer
{
    public static string Serialize(object? value, ReplConfig config)
    {
        if (value == null)
            return "null";

        try
        {
            // Cap top-level IEnumerable values before handing off to Json.NET.
            // We only cap the top level here; deep nesting is bounded by MaxDepth.
            value = CapEnumerable(value, config.MaxEnumerableElements);

            var settings = new JsonSerializerSettings
            {
                MaxDepth = 10,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
                Error = (_, args) => args.ErrorContext.Handled = true,
            };
            return JsonConvert.SerializeObject(value, Formatting.None, settings);
        }
        catch (Exception ex)
        {
            // Last-resort: at least tell the caller something went wrong.
            try
            {
                return JsonConvert.SerializeObject(
                    new { error = $"Serialization failed: {ex.Message}" }
                );
            }
            catch
            {
                return "{\"error\":\"Serialization failed\"}";
            }
        }
    }

    /// <summary>A typed wire value plus a truncation signal for oversized results.</summary>
    internal readonly struct WireValue
    {
        public WireValue(JToken? value, bool truncated, long? byteCount)
        {
            Value = value;
            Truncated = truncated;
            ByteCount = byteCount;
        }

        public JToken? Value { get; }
        public bool Truncated { get; }
        public long? ByteCount { get; }
    }

    /// <summary>
    /// Produces the native JSON token for an eval/subscription wire value so consumers
    /// receive properly typed output. Values whose serialized form exceeds
    /// <see cref="ReplConfig.MaxResultLength"/> bytes are reported as truncated with a
    /// null value rather than emitting partial, invalid JSON.
    /// </summary>
    public static WireValue ToWireValue(object? value, ReplConfig config) =>
        ToWireValue(Serialize(value, config), config);

    /// <summary>
    /// Builds the wire value from an already-serialized JSON string, so callers that
    /// also need the string form (e.g. subscription change-detection) serialize once.
    /// </summary>
    public static WireValue ToWireValue(string serialized, ReplConfig config)
    {
        var byteCount = Encoding.UTF8.GetByteCount(serialized);
        if (config.MaxResultLength > 0 && byteCount > config.MaxResultLength)
            return new WireValue(null, truncated: true, byteCount);

        try
        {
            return new WireValue(JToken.Parse(serialized), truncated: false, byteCount: null);
        }
        catch (JsonReaderException)
        {
            // Serialize always returns valid JSON, but stay defensive: fall back to a
            // string token rather than throwing on the eval hot path.
            return new WireValue(
                JValue.CreateString(serialized),
                truncated: false,
                byteCount: null
            );
        }
    }

    public static string Truncate(string serialized, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;

        var originalByteCount = Encoding.UTF8.GetByteCount(serialized);
        if (originalByteCount <= maxLength)
            return serialized;

        var marker = $" [truncated at {originalByteCount} bytes]";
        var markerByteCount = Encoding.UTF8.GetByteCount(marker);
        if (markerByteCount >= maxLength)
            return serialized.Substring(0, Utf8CharCountWithin(serialized, maxLength));

        var prefixByteLimit = maxLength - markerByteCount;
        var prefixCharCount = Utf8CharCountWithin(serialized, prefixByteLimit);
        return serialized.Substring(0, prefixCharCount) + marker;
    }

    private static object CapEnumerable(object value, int max)
    {
        // Strings are IEnumerable<char> — never cap them.
        // Byte arrays are serialized by Json.NET as base64 — don't unroll them.
        if (value is string || value is byte[])
            return value;

        if (value is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                if (list.Count >= max)
                    break;
                list.Add(item);
            }
            return list;
        }

        return value;
    }

    private static int Utf8CharCountWithin(string value, int maxBytes)
    {
        var bytes = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var currentBytes = Utf8ByteCountAt(value, i, out var charCount);
            if (bytes + currentBytes > maxBytes)
                return i;
            bytes += currentBytes;
            if (charCount == 2)
                i++;
        }

        return value.Length;
    }

    private static int Utf8ByteCountAt(string value, int index, out int charCount)
    {
        var c = value[index];
        if (
            char.IsHighSurrogate(c)
            && index + 1 < value.Length
            && char.IsLowSurrogate(value[index + 1])
        )
        {
            charCount = 2;
            return 4;
        }

        charCount = 1;
        if (c <= 0x7F)
            return 1;
        return c <= 0x7FF ? 2 : 3;
    }
}
