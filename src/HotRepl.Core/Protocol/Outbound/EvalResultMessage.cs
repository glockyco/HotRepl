using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class EvalResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.EvalResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("hasValue")]
    public bool HasValue { get; set; }

    [JsonProperty("value")]
    public string? Value { get; set; }

    [JsonProperty("valueType")]
    public string? ValueType { get; set; }

    [JsonProperty("stdout")]
    public string? Stdout { get; set; }

    [JsonProperty("durationMs")]
    public long DurationMs { get; set; }
}
