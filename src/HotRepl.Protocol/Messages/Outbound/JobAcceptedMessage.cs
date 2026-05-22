using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Acknowledgement that a command job is running.</summary>
public sealed class JobAcceptedMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.JobAccepted;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = "running";
}
