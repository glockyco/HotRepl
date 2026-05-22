using System;
using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Handshake sent by the server immediately after connection.</summary>
public sealed record HandshakeMessage
{
    /// <summary>Creates a representative handshake for tests and fixtures.</summary>
    public static HandshakeMessage CreateForTests() =>
        new()
        {
            ProtocolVersion = 2,
            Host = new HostDescriptor
            {
                Name = "Tests",
                Version = "1.0.0",
                Platform = "Unity Test",
            },
            Evaluator = new EvaluatorDescriptor
            {
                Name = "Roslyn.Script",
                LanguageVersion = "latest",
                PersistentState = true,
                SupportsCompletion = false,
                Cancellation = "cooperative",
            },
            AvailableEvaluators = new[] { "Roslyn.Script" },
            DefaultUsings = new[] { "System" },
            Helpers = new[] { "String[] Help()" },
            Control = new ControlCapabilities
            {
                Supported = true,
                CommandsListChanged = false,
                SchemaValidation = false,
            },
            Limits = RuntimeLimits.CreateDefault(),
            Enforces = EnforcedLimit.Defaults,
        };

    /// <summary>Wire message type.</summary>
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.Handshake;

    /// <summary>Protocol major version.</summary>
    [JsonProperty("protocolVersion")]
    public int ProtocolVersion { get; set; }

    /// <summary>Host adapter metadata.</summary>
    [JsonProperty("host")]
    public HostDescriptor Host { get; set; } = new();

    /// <summary>Active evaluator metadata.</summary>
    [JsonProperty("evaluator")]
    public EvaluatorDescriptor Evaluator { get; set; } = new();

    /// <summary>Names of evaluators available in this runtime.</summary>
    [JsonProperty("availableEvaluators")]
    public string[] AvailableEvaluators { get; set; } = Array.Empty<string>();

    /// <summary>Default using directives injected into eval sessions.</summary>
    [JsonProperty("defaultUsings")]
    public string[] DefaultUsings { get; set; } = Array.Empty<string>();

    /// <summary>Human-readable helper signatures.</summary>
    [JsonProperty("helpers")]
    public string[] Helpers { get; set; } = Array.Empty<string>();

    /// <summary>Typed-command capabilities.</summary>
    [JsonProperty("control")]
    public ControlCapabilities Control { get; set; } = new();

    /// <summary>Runtime limits.</summary>
    [JsonProperty("limits")]
    public RuntimeLimits Limits { get; set; } = RuntimeLimits.CreateDefault();

    /// <summary>Names of limits actively enforced by this runtime.</summary>
    [JsonProperty("enforces")]
    public string[] Enforces { get; set; } = Array.Empty<string>();
}
