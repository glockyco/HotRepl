using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Stable control-plane error or diagnostic envelope.</summary>
public sealed record ControlCommandError(
    string Kind,
    string Code,
    string Message,
    bool Retryable,
    JObject? Details = null);
