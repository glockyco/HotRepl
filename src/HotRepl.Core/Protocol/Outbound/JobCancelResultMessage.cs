using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class JobCancelResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.JobCancelResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("accepted")]
    public bool Accepted { get; set; }

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;
}
