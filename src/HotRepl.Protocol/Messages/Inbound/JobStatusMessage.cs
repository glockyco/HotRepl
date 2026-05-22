using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to poll command job status.</summary>
public sealed class JobStatusMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.JobStatus;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;
}
