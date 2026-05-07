using System;
using HotRepl;
using HotRepl.Evaluator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

/// <summary>Eval error kind discriminants.</summary>
internal static class ErrorKind
{
    public const string Compile = "compile";
    public const string Runtime = "runtime";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
    public const string Unsupported = "unsupported";
}

// ── Inbound ───────────────────────────────────────────────────────────────────

internal sealed class EvalMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.Eval;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("code")] public string Code { get; set; } = string.Empty;
    [JsonProperty("timeoutMs")] public int TimeoutMs { get; set; }
}

internal sealed class CancelMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.Cancel;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
}

internal sealed class ResetMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.Reset;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
}

internal sealed class PingMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.Ping;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
}

internal sealed class CompleteMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.Complete;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("code")] public string Code { get; set; } = string.Empty;
    [JsonProperty("cursorPos")] public int CursorPos { get; set; } = -1;
}

internal sealed class SubscribeMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.Subscribe;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("code")] public string Code { get; set; } = string.Empty;
    [JsonProperty("intervalFrames")] public int IntervalFrames { get; set; } = 1;
    [JsonProperty("onChange")] public bool OnChange { get; set; }
    [JsonProperty("limit")] public int Limit { get; set; }
    [JsonProperty("timeoutMs")] public int TimeoutMs { get; set; }
}

internal sealed class SelectEvaluatorMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.SelectEvaluator;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("evaluator")] public string Evaluator { get; set; } = string.Empty;
}

internal sealed class ControlAuthMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.ControlAuth;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("token")] public string? Token { get; set; }
}

internal sealed class LeaseAcquireMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.LeaseAcquire;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("sessionId")] public string SessionId { get; set; } = string.Empty;
    [JsonProperty("clientName")] public string ClientName { get; set; } = string.Empty;
}

internal sealed class CommandDescribeMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.CommandDescribe;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
}

internal sealed class CommandCallMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.CommandCall;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("leaseId")] public string? LeaseId { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("args")] public JObject Args { get; set; } = new();
    [JsonProperty("timeoutMs")] public int TimeoutMs { get; set; }
    [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
}

internal sealed class JobStatusMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.JobStatus;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("leaseId")] public string? LeaseId { get; set; }
    [JsonProperty("jobId")] public string JobId { get; set; } = string.Empty;
}

internal sealed class JobCancelMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.JobCancel;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("leaseId")] public string? LeaseId { get; set; }
    [JsonProperty("jobId")] public string JobId { get; set; } = string.Empty;
}

// ── Outbound ──────────────────────────────────────────────────────────────────

internal sealed class HandshakeMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.Handshake;
    [JsonProperty("version")] public string Version { get; set; } = string.Empty;
    [JsonProperty("csharpVersion")] public string CsharpVersion { get; set; } = "7.x";
    [JsonProperty("defaultUsings")] public string[] DefaultUsings { get; set; } = Array.Empty<string>();
    [JsonProperty("helpers")] public string[] Helpers { get; set; } = Array.Empty<string>();
    [JsonProperty("evaluator")] public EvaluatorCapabilities? Evaluator { get; set; }
    [JsonProperty("host")] public HostInfo? Host { get; set; }
    [JsonProperty("availableEvaluators")] public string[] AvailableEvaluators { get; set; } = Array.Empty<string>();
}

internal sealed class EvalResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.EvalResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("hasValue")] public bool HasValue { get; set; }
    [JsonProperty("value")] public string? Value { get; set; }
    [JsonProperty("valueType")] public string? ValueType { get; set; }
    [JsonProperty("stdout")] public string? Stdout { get; set; }
    [JsonProperty("durationMs")] public long DurationMs { get; set; }
}

internal sealed class EvalErrorMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.EvalError;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("errorKind")] public string ErrorKind { get; set; } = string.Empty;
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    [JsonProperty("stackTrace")] public string? StackTrace { get; set; }
}

internal sealed class ResetResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.ResetResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("success")] public bool Success { get; set; }
}

internal sealed class SelectEvaluatorResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.SelectEvaluatorResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("evaluator")] public string Evaluator { get; set; } = string.Empty;
}

internal sealed class SelectEvaluatorErrorMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.SelectEvaluatorError;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("errorKind")] public string ErrorKind { get; set; } = string.Empty;
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
}

internal sealed class PongMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.Pong;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
}

internal sealed class CompleteResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.CompleteResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("completions")] public string[] Completions { get; set; } = Array.Empty<string>();
    [JsonProperty("durationMs")] public long DurationMs { get; set; }
}

internal sealed class SubscribeResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.SubscribeResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("seq")] public int Seq { get; set; }
    [JsonProperty("hasValue")] public bool HasValue { get; set; }
    [JsonProperty("value")] public string? Value { get; set; }
    [JsonProperty("valueType")] public string? ValueType { get; set; }
    [JsonProperty("durationMs")] public long DurationMs { get; set; }
    [JsonProperty("final")] public bool Final { get; set; }
}

internal sealed class SubscribeErrorMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.SubscribeError;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("seq")] public int Seq { get; set; }
    [JsonProperty("errorKind")] public string ErrorKind { get; set; } = string.Empty;
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    [JsonProperty("final")] public bool Final { get; set; }
}

internal sealed class AssemblyReloadMessage
{
    [JsonProperty("type")] public string Type => MessageType.AssemblyReload;
    [JsonProperty("assembly")] public string? Assembly { get; set; }
    [JsonProperty("message")] public string? Message { get; set; }
}

internal sealed class ControlAuthResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.ControlAuthResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("ok")] public bool Ok { get; set; }
    [JsonProperty("sessionId")] public string? SessionId { get; set; }
    [JsonProperty("error")] public ControlErrorMessage? Error { get; set; }
}

internal sealed class LeaseAcquireResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.LeaseAcquireResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("ok")] public bool Ok { get; set; }
    [JsonProperty("leaseId")] public string? LeaseId { get; set; }
    [JsonProperty("error")] public ControlErrorMessage? Error { get; set; }
}

internal sealed class CommandDescriptorMessage
{
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("version")] public int Version { get; set; }
    [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
    [JsonProperty("mutatesState")] public bool MutatesState { get; set; }
    [JsonProperty("argsSchema")] public JObject ArgsSchema { get; set; } = new();
    [JsonProperty("resultSchema")] public JObject ResultSchema { get; set; } = new();
}

internal sealed class CommandDescribeResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.CommandDescribeResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("commands")] public CommandDescriptorMessage[] Commands { get; set; } = Array.Empty<CommandDescriptorMessage>();
}

internal sealed class CommandResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.CommandResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    [JsonProperty("result")] public JObject Result { get; set; } = new();
    [JsonProperty("artifacts")] public ArtifactRefMessage[] Artifacts { get; set; } = Array.Empty<ArtifactRefMessage>();
    [JsonProperty("diagnostics")] public ControlErrorMessage[] Diagnostics { get; set; } = Array.Empty<ControlErrorMessage>();
}

internal sealed class CommandErrorMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.CommandError;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    [JsonProperty("error")] public ControlErrorMessage Error { get; set; } = new();
    [JsonProperty("diagnostics")] public ControlErrorMessage[] Diagnostics { get; set; } = Array.Empty<ControlErrorMessage>();
}

internal sealed class CommandAcceptedMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.CommandAccepted;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("jobId")] public string JobId { get; set; } = string.Empty;
    [JsonProperty("state")] public string State { get; set; } = string.Empty;
}

internal sealed class JobStatusResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.JobStatusResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("jobId")] public string JobId { get; set; } = string.Empty;
    [JsonProperty("state")] public string State { get; set; } = string.Empty;
    [JsonProperty("progress")] public JObject? Progress { get; set; }
}

internal sealed class JobResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.JobResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("jobId")] public string JobId { get; set; } = string.Empty;
    [JsonProperty("state")] public string State { get; set; } = string.Empty;
    [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    [JsonProperty("result")] public JObject Result { get; set; } = new();
    [JsonProperty("artifacts")] public ArtifactRefMessage[] Artifacts { get; set; } = Array.Empty<ArtifactRefMessage>();
    [JsonProperty("diagnostics")] public ControlErrorMessage[] Diagnostics { get; set; } = Array.Empty<ControlErrorMessage>();
}

internal sealed class JobCancelResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.JobCancelResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("accepted")] public bool Accepted { get; set; }
    [JsonProperty("state")] public string State { get; set; } = string.Empty;
}

internal sealed class JobEventMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.JobEvent;
    [JsonProperty("jobId")] public string JobId { get; set; } = string.Empty;
    [JsonProperty("sequence")] public long Sequence { get; set; }
    [JsonProperty("state")] public string State { get; set; } = string.Empty;
    [JsonProperty("progress")] public JObject? Progress { get; set; }
    [JsonProperty("message")] public string? Message { get; set; }
}

internal sealed class ArtifactRefMessage
{
    [JsonProperty("logicalName")] public string LogicalName { get; set; } = string.Empty;
    [JsonProperty("uri")] public string Uri { get; set; } = string.Empty;
    [JsonProperty("path")] public string? Path { get; set; }
    [JsonProperty("contentType")] public string ContentType { get; set; } = string.Empty;
    [JsonProperty("byteSize")] public long ByteSize { get; set; }
    [JsonProperty("sha256")] public string Sha256 { get; set; } = string.Empty;
    [JsonProperty("finalized")] public bool Finalized { get; set; }
}

internal sealed class ControlErrorMessage
{
    [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
    [JsonProperty("code")] public string Code { get; set; } = string.Empty;
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    [JsonProperty("retryable")] public bool Retryable { get; set; }
    [JsonProperty("details")] public JObject? Details { get; set; }
}
