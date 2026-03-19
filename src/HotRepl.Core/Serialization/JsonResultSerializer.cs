using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace HotRepl.Serialization;

/// <summary>
/// Converts arbitrary runtime values to JSON strings using Newtonsoft.Json.
/// Caps IEnumerables to prevent unbounded output. Truncates the final string
/// to MaxResultLength and appends a diagnostic marker.
/// Never throws — serialization failures are returned as a JSON error object.
/// </summary>
internal sealed class JsonResultSerializer : IResultSerializer
{
    public string Serialize(object? value, ReplConfig config)
    {
        if (value == null)
            return "null";

        try
        {
            // Cap top-level enumerables before handing off to Json.NET.
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
            { return JsonConvert.SerializeObject(new { error = $"Serialization failed: {ex.Message}" }); }
            catch { return "{\"error\":\"Serialization failed\"}"; }
        }
    }

    public string Truncate(string serialized, int maxLength)
    {
        if (serialized.Length <= maxLength)
            return serialized;
        return serialized.Substring(0, maxLength) + $" [truncated at {serialized.Length} chars]";
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static object CapEnumerable(object value, int max)
    {
        // Strings are IEnumerable<char> — never cap them.
        if (value is string)
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
}
