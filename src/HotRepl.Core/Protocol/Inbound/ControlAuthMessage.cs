using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class ControlAuthMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.ControlAuth;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("token")]
    public string? Token { get; set; }
}
