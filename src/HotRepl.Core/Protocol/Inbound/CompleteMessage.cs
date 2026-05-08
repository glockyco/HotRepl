using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class CompleteMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.Complete;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("cursorPos")]
    public int CursorPos { get; set; } = -1;
}
