using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class AssemblyReloadMessage
{
    [JsonProperty("type")]
    public string Type => MessageType.AssemblyReload;

    [JsonProperty("assembly")]
    public string? Assembly { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }
}
