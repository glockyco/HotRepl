using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to reset evaluator state.</summary>
public sealed class ResetMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.Reset;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}
