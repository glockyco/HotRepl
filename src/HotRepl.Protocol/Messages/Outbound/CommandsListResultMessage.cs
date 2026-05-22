using System;
using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Registered command list response.</summary>
public sealed class CommandsListResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.CommandsListResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("commands")]
    public CommandSummary[] Commands { get; set; } = Array.Empty<CommandSummary>();

    [JsonProperty("since")]
    public string? Since { get; set; }
}
