using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>
/// Serializes outbound messages and deserializes inbound ones.
/// ParseType() uses a JObject parse rather than a streaming reader for simplicity
/// and reliability — inbound messages are small and the overhead is negligible.
/// </summary>
internal static class MessageSerializer
{
    /// <summary>Reads the "type" field without fully deserializing the message.</summary>
    public static string ParseType(string rawJson)
    {
        try
        { return JObject.Parse(rawJson)["type"]?.Value<string>() ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static T Deserialize<T>(string rawJson) =>
        JsonConvert.DeserializeObject<T>(rawJson)!;

    public static string Serialize<T>(T message) =>
        JsonConvert.SerializeObject(message, Formatting.None);
}
