using Newtonsoft.Json;

namespace HotRepl.Protocol
{
    // ── Inbound (client → server) ──────────────────────────────────────

    /// <summary>Request to evaluate a C# expression or statement.</summary>
    internal sealed class EvalRequest
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "eval";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("timeoutMs")]
        public int TimeoutMs { get; set; } = 10_000;
    }

    /// <summary>Request to cancel a running evaluation.</summary>
    internal sealed class CancelRequest
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "cancel";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>Request to reset the evaluator state.</summary>
    internal sealed class ResetRequest
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "reset";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>Heartbeat ping from the client.</summary>
    internal sealed class PingRequest
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "ping";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
    }

    // ── Outbound (server → client) ─────────────────────────────────────

    /// <summary>Sent on connection to advertise server capabilities.</summary>
    internal sealed class HandshakeMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "handshake";

        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("csharpVersion")]
        public string CsharpVersion { get; set; } = "7.x";

        [JsonProperty("defaultUsings")]
        public string[] DefaultUsings { get; set; } = System.Array.Empty<string>();

        [JsonProperty("helpers")]
        public string[] Helpers { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>Successful evaluation result.</summary>
    internal sealed class EvalResultMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "eval_result";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("hasValue")]
        public bool HasValue { get; set; }

        [JsonProperty("value")]
        public string? Value { get; set; }

        [JsonProperty("valueType")]
        public string? ValueType { get; set; }

        [JsonProperty("stdout")]
        public string? Stdout { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }
    }

    /// <summary>Evaluation error (compile error, runtime exception, timeout).</summary>
    internal sealed class EvalErrorMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "eval_error";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("errorKind")]
        public string ErrorKind { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("stackTrace")]
        public string? StackTrace { get; set; }
    }

    /// <summary>Response to a reset request.</summary>
    internal sealed class ResetResultMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "reset_result";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("success")]
        public bool Success { get; set; }
    }

    /// <summary>Heartbeat pong response.</summary>
    internal sealed class PongMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "pong";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
    }

    // =====================================================================
    // Autocomplete
    // =====================================================================

    /// <summary>Autocomplete request — does not execute code.</summary>
    internal sealed class CompleteRequest
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "complete";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("cursorPos")]
        public int CursorPos { get; set; } = -1; // -1 = end of code
    }

    /// <summary>Autocomplete result with completion candidates.</summary>
    internal sealed class CompleteResultMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "complete_result";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("completions")]
        public string[] Completions { get; set; } = System.Array.Empty<string>();

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }
    }


    // =====================================================================
    // Subscriptions (watches, breakpoints, hooks)
    // =====================================================================

    /// <summary>Subscribe to repeated evaluation of an expression.</summary>
    internal sealed class SubscribeRequest
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "subscribe";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("intervalFrames")]
        public int IntervalFrames { get; set; } = 1;

        [JsonProperty("onChange")]
        public bool OnChange { get; set; }

        [JsonProperty("limit")]
        public int Limit { get; set; } // 0 = unlimited

        [JsonProperty("timeoutMs")]
        public int TimeoutMs { get; set; } = 10000;
    }

    /// <summary>Subscription evaluation result (pushed repeatedly).</summary>
    internal sealed class SubscribeResultMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "subscribe_result";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("seq")]
        public int Seq { get; set; }

        [JsonProperty("hasValue")]
        public bool HasValue { get; set; }

        [JsonProperty("value")]
        public string? Value { get; set; }

        [JsonProperty("valueType")]
        public string? ValueType { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("final")]
        public bool Final { get; set; }
    }

    /// <summary>Subscription evaluation error.</summary>
    internal sealed class SubscribeErrorMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "subscribe_error";

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("seq")]
        public int Seq { get; set; }

        [JsonProperty("errorKind")]
        public string ErrorKind { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("final")]
        public bool Final { get; set; }
    }

}
