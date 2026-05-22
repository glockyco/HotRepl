using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Reset response.</summary>
public sealed class ResetResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.ResetResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }
}
