using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>Terminal synchronous command response.</summary>
public sealed class CommandResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.CommandResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = "ok";

    [JsonProperty("output")]
    public JToken? Output { get; set; }

    [JsonProperty("artifacts")]
    public IDictionary<string, ArtifactRef> Artifacts { get; set; } =
        new Dictionary<string, ArtifactRef>(StringComparer.Ordinal);

    [JsonProperty("error")]
    public HotReplErrorEnvelope? Error { get; set; }

    [JsonProperty("durationMs")]
    public long DurationMs { get; set; }
}
