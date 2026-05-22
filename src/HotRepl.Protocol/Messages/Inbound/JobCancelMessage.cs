using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to cancel a command job.</summary>
public sealed class JobCancelMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.JobCancel;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;
}
