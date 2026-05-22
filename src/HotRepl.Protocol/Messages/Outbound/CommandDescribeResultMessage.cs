using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Command descriptor response.</summary>
public sealed record CommandDescribeResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.CommandDescribeResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("descriptor")]
    public CommandDescriptor Descriptor { get; set; } = new();
}
