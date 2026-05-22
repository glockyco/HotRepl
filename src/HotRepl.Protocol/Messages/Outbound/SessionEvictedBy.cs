using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Metadata about the client that displaced a previous session.</summary>
public sealed record SessionEvictedBy
{
    /// <summary>Name of the replacing client when known.</summary>
    [JsonProperty("clientName")]
    public string? ClientName { get; set; }
}
