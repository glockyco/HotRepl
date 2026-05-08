using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class ResetResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.ResetResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }
}
