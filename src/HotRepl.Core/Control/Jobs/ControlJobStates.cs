namespace HotRepl.Control.Jobs;

internal static class ControlJobStates
{
    public const string Accepted = "accepted";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelling = "cancelling";
    public const string Cancelled = "cancelled";
}
