using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

internal sealed class JobEventMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.JobEvent;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonProperty("sequence")]
    public long Sequence { get; set; }

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;

    [JsonProperty("progress")]
    public JObject? Progress { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }
}
