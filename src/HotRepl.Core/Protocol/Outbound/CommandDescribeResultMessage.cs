using System;
using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class CommandDescribeResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.CommandDescribeResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("commands")]
    public CommandDescriptorMessage[] Commands { get; set; } =
        Array.Empty<CommandDescriptorMessage>();
}
