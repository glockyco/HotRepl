using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class CommandAcceptedMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.CommandAccepted;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;
}
