using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class JobCancelMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.JobCancel;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonIgnore]
    public string? LeaseId { get; set; }

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;
}
