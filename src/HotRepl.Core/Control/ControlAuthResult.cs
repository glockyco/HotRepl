namespace HotRepl.Control;

internal sealed record ControlAuthResult(bool Ok, string? SessionId, ControlCommandError? Error)
{
    public static ControlAuthResult Succeeded(string sessionId) => new(true, sessionId, null);

    public static ControlAuthResult Failed(ControlCommandError error) => new(false, null, error);
}
