using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class EvalMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.Eval;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("timeoutMs")]
    public int TimeoutMs { get; set; }
}
