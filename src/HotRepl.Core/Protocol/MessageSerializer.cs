using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>
/// Serializes outbound messages and deserializes inbound ones.
///
/// ParseType contract (fail fast):
///   - Invalid JSON       → throws <see cref="JsonException"/>
///   - Missing type field → throws <see cref="InvalidOperationException"/>
///
/// Serialize omits null properties to keep wire payloads minimal.
/// </summary>
internal static class MessageSerializer
{
    private static readonly JsonSerializerSettings _outboundSettings = new()
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>
    /// Extracts the "type" discriminant from a raw JSON frame.
    /// Throws on invalid JSON or a missing "type" field — callers that receive
    /// untrusted input should wrap this in a try/catch.
    /// </summary>
    public static string ParseType(string rawJson)
    {
        var obj = JObject.Parse(rawJson); // throws JsonException on bad JSON
        return obj["type"]?.Value<string>()
            ?? throw new InvalidOperationException($"Message has no 'type' field: {rawJson}");
    }

    public static T Deserialize<T>(string rawJson) =>
        JsonConvert.DeserializeObject<T>(rawJson)!;

    /// <summary>Serializes an outbound message, omitting null properties.</summary>
    public static string Serialize<T>(T message) =>
        JsonConvert.SerializeObject(message, _outboundSettings);
}
