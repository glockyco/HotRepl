using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Command job cancellation response.</summary>
public sealed record JobCancelResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.JobCancelResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonProperty("accepted")]
    public bool Accepted { get; set; }

    [JsonProperty("state")]
    public string State { get; set; } = "running";
}
