using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to cancel an eval or subscription.</summary>
public sealed class CancelMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.Cancel;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("targetId")]
    public string TargetId { get; set; } = string.Empty;
}
