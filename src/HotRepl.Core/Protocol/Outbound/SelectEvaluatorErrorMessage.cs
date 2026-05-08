using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class SelectEvaluatorErrorMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.SelectEvaluatorError;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("errorKind")]
    public string ErrorKind { get; set; } = string.Empty;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}
