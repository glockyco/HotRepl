using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to complete C# code at a cursor position.</summary>
public sealed class CompleteMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.Complete;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("cursor")]
    public int? Cursor { get; set; }
}
