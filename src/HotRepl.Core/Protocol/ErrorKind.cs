namespace HotRepl.Protocol;

/// <summary>Eval error kind discriminants.</summary>
internal static class ErrorKind
{
    public const string Compile = "compile";
    public const string Runtime = "runtime";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
    public const string Unsupported = "unsupported";
}
