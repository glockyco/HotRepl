using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class PongMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.Pong;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}
