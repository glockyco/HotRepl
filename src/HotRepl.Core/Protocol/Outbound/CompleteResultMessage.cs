using System;
using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class CompleteResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.CompleteResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("completions")]
    public string[] Completions { get; set; } = Array.Empty<string>();

    [JsonProperty("durationMs")]
    public long DurationMs { get; set; }
}
