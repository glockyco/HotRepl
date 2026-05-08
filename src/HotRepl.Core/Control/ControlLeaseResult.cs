namespace HotRepl.Control;

internal sealed record ControlLeaseResult(bool Ok, string? LeaseId, ControlCommandError? Error)
{
    public static ControlLeaseResult Succeeded(string leaseId) => new(true, leaseId, null);

    public static ControlLeaseResult Failed(ControlCommandError error) => new(false, null, error);
}
