using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class EvalErrorMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.EvalError;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("error")]
    public ControlErrorMessage Error { get; set; } = new();

    [JsonIgnore]
    public string ErrorKind
    {
        get => Error.Kind;
        set => Error.Kind = value;
    }

    [JsonIgnore]
    public string Message
    {
        get => Error.Message;
        set
        {
            Error.Message = value;
            if (string.IsNullOrEmpty(Error.Code))
                Error.Code = "evalFailed";
        }
    }

    [JsonIgnore]
    public string? StackTrace { get; set; }
}
