using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class SubscribeErrorMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.SubscribeError;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("seq")]
    public int Seq { get; set; }

    [JsonProperty("errorKind")]
    public string ErrorKind { get; set; } = string.Empty;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("final")]
    public bool Final { get; set; }
}
