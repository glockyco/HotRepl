using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>Current command job status.</summary>
public sealed record JobStatusResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.JobStatusResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = "running";

    [JsonProperty("progress")]
    public JToken? Progress { get; set; }

    [JsonProperty("error")]
    public HotReplErrorEnvelope? Error { get; set; }
}
