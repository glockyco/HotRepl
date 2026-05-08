using System;
using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class CommandErrorMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.CommandError;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("error")]
    public ControlErrorMessage Error { get; set; } = new();

    [JsonProperty("diagnostics")]
    public ControlErrorMessage[] Diagnostics { get; set; } = Array.Empty<ControlErrorMessage>();
}
