using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

internal sealed class JobResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.JobResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("output")]
    public JObject Output { get; set; } = new();

    [JsonProperty("artifacts")]
    public IDictionary<string, ArtifactRefMessage> Artifacts { get; set; } =
        new Dictionary<string, ArtifactRefMessage>(StringComparer.Ordinal);

    [JsonProperty("error")]
    public ControlErrorMessage? Error { get; set; }

    [JsonIgnore]
    public JObject Result
    {
        get => Output;
        set => Output = value;
    }

    [JsonIgnore]
    public ControlErrorMessage[] Diagnostics { get; set; } = Array.Empty<ControlErrorMessage>();
}
