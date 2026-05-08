using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class ResetMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.Reset;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}
