using System;
using HotRepl;
using HotRepl.Evaluator;
using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class HandshakeMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.Handshake;

    [JsonProperty("version")]
    public string Version { get; set; } = string.Empty;

    [JsonProperty("csharpVersion")]
    public string CsharpVersion { get; set; } = "7.x";

    [JsonProperty("defaultUsings")]
    public string[] DefaultUsings { get; set; } = Array.Empty<string>();

    [JsonProperty("helpers")]
    public string[] Helpers { get; set; } = Array.Empty<string>();

    [JsonProperty("evaluator")]
    public EvaluatorCapabilities? Evaluator { get; set; }

    [JsonProperty("host")]
    public HostInfo? Host { get; set; }

    [JsonProperty("availableEvaluators")]
    public string[] AvailableEvaluators { get; set; } = Array.Empty<string>();

    [JsonProperty("controlPlane")]
    public ControlPlaneHandshake? ControlPlane { get; set; }
}
