using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Compact command metadata returned by commands_list.</summary>
public sealed record CommandSummary
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("majorVersion")]
    public int MajorVersion { get; set; }

    [JsonProperty("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonProperty("mutatesState")]
    public bool MutatesState { get; set; }
}
