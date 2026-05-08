using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class SelectEvaluatorMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.SelectEvaluator;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("evaluator")]
    public string Evaluator { get; set; } = string.Empty;
}
