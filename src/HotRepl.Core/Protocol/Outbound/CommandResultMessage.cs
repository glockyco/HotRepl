using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

internal sealed class CommandResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.CommandResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("result")]
    public JObject Result { get; set; } = new();

    [JsonProperty("artifacts")]
    public ArtifactRefMessage[] Artifacts { get; set; } = Array.Empty<ArtifactRefMessage>();

    [JsonProperty("diagnostics")]
    public ControlErrorMessage[] Diagnostics { get; set; } = Array.Empty<ControlErrorMessage>();
}
