using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

internal sealed class JobStatusResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.JobStatusResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;

    [JsonProperty("progress")]
    public JObject? Progress { get; set; }
}
