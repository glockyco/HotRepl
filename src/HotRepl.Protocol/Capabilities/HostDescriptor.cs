using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Runtime host metadata advertised in the v2 handshake.</summary>
public sealed record HostDescriptor
{
    /// <summary>Host adapter name.</summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Host adapter version.</summary>
    [JsonProperty("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Runtime platform description.</summary>
    [JsonProperty("platform")]
    public string Platform { get; set; } = string.Empty;
}
