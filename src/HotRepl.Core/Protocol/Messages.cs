using System;
using Newtonsoft.Json;

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

    // Outbound
    public const string Handshake = "handshake";
    public const string EvalResult = "eval_result";
    public const string EvalError = "eval_error";
    public const string ResetResult = "reset_result";
    public const string Pong = "pong";
    public const string CompleteResult = "complete_result";
    public const string SubscribeResult = "subscribe_result";
    public const string SubscribeError = "subscribe_error";
}

/// <summary>Eval error kind discriminants.</summary>
internal static class ErrorKind
{
    public const string Compile = "compile";
    public const string Runtime = "runtime";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
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

// ── Outbound ──────────────────────────────────────────────────────────────────

internal sealed class HandshakeMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.Handshake;
    [JsonProperty("version")] public string Version { get; set; } = string.Empty;
    [JsonProperty("csharpVersion")] public string CsharpVersion { get; set; } = "7.x";
    [JsonProperty("defaultUsings")] public string[] DefaultUsings { get; set; } = Array.Empty<string>();
    [JsonProperty("helpers")] public string[] Helpers { get; set; } = Array.Empty<string>();
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
