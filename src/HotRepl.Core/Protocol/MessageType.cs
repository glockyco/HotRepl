namespace HotRepl.Protocol;

/// <summary>Wire-format type discriminants for all protocol messages.</summary>
internal static class MessageType
{
    // Inbound
    public const string Eval = "eval";
    public const string Cancel = "cancel";
    public const string Reset = "reset";
    public const string Ping = "ping";
    public const string Complete = "complete";
    public const string Subscribe = "subscribe";
    public const string SelectEvaluator = "select_evaluator";
    public const string ControlAuth = "control_auth";
    public const string LeaseAcquire = "lease_acquire";
    public const string CommandDescribe = "command_describe";
    public const string CommandCall = "command_call";
    public const string JobStatus = "job_status";
    public const string JobCancel = "job_cancel";

    // Outbound
    public const string Handshake = "handshake";
    public const string EvalResult = "eval_result";
    public const string EvalError = "eval_error";
    public const string ResetResult = "reset_result";
    public const string Pong = "pong";
    public const string CompleteResult = "complete_result";
    public const string SubscribeResult = "subscribe_result";
    public const string SubscribeError = "subscribe_error";
    public const string AssemblyReload = "assembly_reload";
    public const string SelectEvaluatorResult = "select_evaluator_result";
    public const string SelectEvaluatorError = "select_evaluator_error";
    public const string ControlAuthResult = "control_auth_result";
    public const string LeaseAcquireResult = "lease_acquire_result";
    public const string CommandDescribeResult = "command_describe_result";
    public const string CommandResult = "command_result";
    public const string CommandError = "command_error";
    public const string CommandAccepted = "command_accepted";
    public const string JobStatusResult = "job_status_result";
    public const string JobResult = "job_result";
    public const string JobCancelResult = "job_cancel_result";
    public const string JobEvent = "job_event";
}
