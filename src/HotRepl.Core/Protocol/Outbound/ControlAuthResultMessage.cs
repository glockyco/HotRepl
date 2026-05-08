using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class ControlAuthResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.ControlAuthResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("ok")]
    public bool Ok { get; set; }

    [JsonProperty("sessionId")]
    public string? SessionId { get; set; }

    [JsonProperty("error")]
    public ControlErrorMessage? Error { get; set; }
}
