using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Raised when a job reaches a failed terminal state.</summary>
public sealed class HotReplJobFailedException : HotReplException
{
    /// <summary>Create a job failure.</summary>
    public HotReplJobFailedException(
        string jobId,
        string code,
        string message,
        JToken? details = null
    )
        : base(HotReplErrorKind.PreconditionFailed, code, message, retryable: false, details)
    {
        JobId = jobId;
    }

    /// <summary>Failed job ID.</summary>
    public string JobId { get; }
}
