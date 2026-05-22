using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Typed-command capabilities advertised in the v2 handshake.</summary>
public sealed class ControlCapabilities
{
    /// <summary>Whether typed commands are supported.</summary>
    [JsonProperty("supported")]
    public bool Supported { get; set; }

    /// <summary>Whether the command catalog can change during a session.</summary>
    [JsonProperty("commandsListChanged")]
    public bool CommandsListChanged { get; set; }

    /// <summary>Whether command schemas are validated by the runtime.</summary>
    [JsonProperty("schemaValidation")]
    public bool SchemaValidation { get; set; }
}
