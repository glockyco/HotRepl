using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>Successful eval response.</summary>
public sealed record EvalResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.EvalResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("hasValue")]
    public bool HasValue { get; set; }

    [JsonProperty("value")]
    public JToken? Value { get; set; }

    [JsonProperty("valueType")]
    public string? ValueType { get; set; }

    [JsonProperty("stdout")]
    public string? Stdout { get; set; }

    [JsonProperty("durationMs")]
    public long DurationMs { get; set; }
}
