using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class SelectEvaluatorResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.SelectEvaluatorResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("evaluator")]
    public string Evaluator { get; set; } = string.Empty;
}
