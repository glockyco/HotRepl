using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class CancelMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.Cancel;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}
