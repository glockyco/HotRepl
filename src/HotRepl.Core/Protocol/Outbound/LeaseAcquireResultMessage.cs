using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class LeaseAcquireResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.LeaseAcquireResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("ok")]
    public bool Ok { get; set; }

    [JsonProperty("leaseId")]
    public string? LeaseId { get; set; }

    [JsonProperty("error")]
    public ControlErrorMessage? Error { get; set; }
}
