using Newtonsoft.Json;

namespace HotRepl;

/// <summary>Metadata describing the host adapter that embedded the REPL engine.</summary>
public sealed class HostInfo
{
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    [JsonProperty("version")]
    public string Version { get; init; } = string.Empty;

    [JsonProperty("runtime")]
    public string Runtime { get; init; } = string.Empty;

    [JsonProperty("platform")]
    public string Platform { get; init; } = string.Empty;
}
