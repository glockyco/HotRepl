using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to evaluate C# code.</summary>
public sealed class EvalMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.Eval;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("timeoutMs")]
    public int? TimeoutMs { get; set; }

    [JsonProperty("evaluator")]
    public string? Evaluator { get; set; }
}
