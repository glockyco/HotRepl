namespace HotRepl.Protocol;

/// <summary>Wire-format type discriminants for the v2 HotRepl protocol.</summary>
public static class MessageType
{
    /// <summary>Server handshake sent immediately after connection.</summary>
    public const string Handshake = "handshake";

    /// <summary>Notification sent to a displaced connection before close.</summary>
    public const string SessionEvicted = "session_evicted";

    /// <summary>Addressed protocol-level error response.</summary>
    public const string Error = "error";

    /// <summary>Evaluate C# code.</summary>
    public const string Eval = "eval";

    /// <summary>Request C# completion candidates.</summary>
    public const string Complete = "complete";

    /// <summary>Reset evaluator state.</summary>
    public const string Reset = "reset";

    /// <summary>Subscribe to repeated expression evaluation.</summary>
    public const string Subscribe = "subscribe";

    /// <summary>Cancel an eval or subscription by id.</summary>
    public const string Cancel = "cancel";

    /// <summary>Successful eval response.</summary>
    public const string EvalResult = "eval_result";

    /// <summary>Failed eval response.</summary>
    public const string EvalError = "eval_error";

    /// <summary>Completion response.</summary>
    public const string CompleteResult = "complete_result";

    /// <summary>Reset response.</summary>
    public const string ResetResult = "reset_result";

    /// <summary>Subscription tick response.</summary>
    public const string SubscribeResult = "subscribe_result";

    /// <summary>Subscription error response.</summary>
    public const string SubscribeError = "subscribe_error";

    /// <summary>Unsolicited assembly reload notification.</summary>
    public const string AssemblyReload = "assembly_reload";

    /// <summary>List registered commands.</summary>
    public const string CommandsList = "commands_list";

    /// <summary>Describe one registered command.</summary>
    public const string CommandDescribe = "command_describe";

    /// <summary>Invoke a registered command.</summary>
    public const string CommandCall = "command_call";

    /// <summary>Poll job status.</summary>
    public const string JobStatus = "job_status";

    /// <summary>Cancel a job.</summary>
    public const string JobCancel = "job_cancel";

    /// <summary>Registered command list response.</summary>
    public const string CommandsListResult = "commands_list_result";

    /// <summary>Command descriptor response.</summary>
    public const string CommandDescribeResult = "command_describe_result";

    /// <summary>Terminal synchronous command response.</summary>
    public const string CommandResult = "command_result";

    /// <summary>Job command acknowledgement.</summary>
    public const string JobAccepted = "job_accepted";

    /// <summary>Job status response.</summary>
    public const string JobStatusResult = "job_status_result";

    /// <summary>Terminal job response.</summary>
    public const string JobResult = "job_result";

    /// <summary>Job cancellation response.</summary>
    public const string JobCancelResult = "job_cancel_result";

    /// <summary>Query server-side journal entries.</summary>
    public const string JournalQuery = "journal_query";

    /// <summary>Server-side journal response.</summary>
    public const string JournalQueryResult = "journal_query_result";
}
