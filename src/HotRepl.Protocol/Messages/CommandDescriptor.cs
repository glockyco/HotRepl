using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>Full machine-readable metadata for one typed command.</summary>
public sealed record CommandDescriptor
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("majorVersion")]
    public int MajorVersion { get; set; }

    [JsonProperty("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonProperty("mutatesState")]
    public bool MutatesState { get; set; }

    [JsonProperty("inputSchema")]
    public JObject InputSchema { get; set; } = new();

    [JsonProperty("outputSchema")]
    public JObject OutputSchema { get; set; } = new();

    [JsonProperty("artifactsSchema")]
    public JObject ArtifactsSchema { get; set; } = new();

    [JsonProperty("cancellation")]
    public string? Cancellation { get; set; }
}
