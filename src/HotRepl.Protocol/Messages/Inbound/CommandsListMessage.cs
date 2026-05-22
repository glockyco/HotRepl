using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to list registered commands.</summary>
public sealed record CommandsListMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.CommandsList;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("since")]
    public string? Since { get; set; }
}
