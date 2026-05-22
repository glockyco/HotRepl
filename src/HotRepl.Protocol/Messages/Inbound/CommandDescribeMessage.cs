using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to describe one registered command.</summary>
public sealed class CommandDescribeMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.CommandDescribe;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}
