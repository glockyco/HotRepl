using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol.Serialization;

/// <summary>JSON serializer helpers for v2 protocol messages.</summary>
public static class ProtocolMessageSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>Serializes a protocol message to compact JSON.</summary>
    public static string Serialize(object message) => JsonConvert.SerializeObject(message, Settings);

    /// <summary>Deserializes compact JSON to a protocol message.</summary>
    public static T Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings)
        ?? throw new InvalidOperationException("Message body was null.");

    /// <summary>Parses the wire type discriminator without deserializing the full message.</summary>
    public static string ParseType(string json)
    {
        var obj = JObject.Parse(json);
        var token = obj["type"];
        if (token == null || token.Type != JTokenType.String)
            throw new InvalidOperationException("Protocol message must include a string 'type' field.");

        return token.Value<string>()!;
    }

    /// <summary>Parses the optional correlation id without deserializing the full message.</summary>
    public static string? ParseId(string json)
    {
        var obj = JObject.Parse(json);
        return obj["id"]?.Value<string>();
    }
}
