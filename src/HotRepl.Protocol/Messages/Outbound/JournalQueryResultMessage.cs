using System;
using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Server-side journal query response.</summary>
public sealed record JournalQueryResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.JournalQueryResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("entries")]
    public JournalEntry[] Entries { get; set; } = Array.Empty<JournalEntry>();
}
