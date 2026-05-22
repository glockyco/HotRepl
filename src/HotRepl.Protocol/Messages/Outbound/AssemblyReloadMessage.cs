using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Unsolicited assembly reload notification.</summary>
public sealed class AssemblyReloadMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.AssemblyReload;

    [JsonProperty("assembly")]
    public string? Assembly { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}
