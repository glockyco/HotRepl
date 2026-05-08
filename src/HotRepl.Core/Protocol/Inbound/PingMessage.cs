using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class PingMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.Ping;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}
