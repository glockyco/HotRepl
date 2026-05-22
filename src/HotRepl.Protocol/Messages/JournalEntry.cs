using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Metadata-only server-side journal entry.</summary>
public sealed record JournalEntry
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("durationMs")]
    public long DurationMs { get; set; }

    [JsonProperty("errorKind")]
    public string? ErrorKind { get; set; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; set; } = string.Empty;
}
