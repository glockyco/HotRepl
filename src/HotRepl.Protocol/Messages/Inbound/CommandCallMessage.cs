using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>Request to invoke a typed command.</summary>
public sealed class CommandCallMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.CommandCall;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("args")]
    public JObject Args { get; set; } = new();

    [JsonProperty("timeoutMs")]
    public int? TimeoutMs { get; set; }
}
