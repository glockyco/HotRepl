using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class CommandDescribeMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.CommandDescribe;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}
