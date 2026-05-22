extern alias HotReplProtocolV2;

using System;
using System.Text;
using HotRepl.Engine.Commands;
using HotRepl.Evaluator;
using HotRepl.Protocol;
using ProtocolV2 = HotReplProtocolV2::HotRepl.Protocol;
using ProtocolMessageSerializer = HotReplProtocolV2::HotRepl.Protocol.Serialization.ProtocolMessageSerializer;

namespace HotRepl.Server;

/// <summary>
/// Translates inbound JSON frames into typed engine commands and enqueues them.
/// Cancel is the only message that bypasses the queue (it must pre-empt running evals).
/// All other messages are queued for Tick() to process on the main thread.
/// </summary>
internal sealed class MessageRouter
{
    private readonly Action<IEngineCommand> _enqueueCommand;
    private readonly Action<EvalJob> _enqueueEval;
    private readonly Action<string> _cancelEval;
    private readonly Action<Guid, string> _sendProtocolError;
    private readonly ReplConfig _config;
    private readonly Func<int> _queuedCommandCount;
    private readonly Action<string> _log;

    public MessageRouter(ReplEngine engine, Action<string> log)
        : this(
            engine.EnqueueCommand,
            engine.EnqueueEval,
            engine.CancelEval,
            engine.SendProtocolError,
            engine.Config,
            () => engine.QueuedCommandCount,
            log
        ) { }

    private MessageRouter(
        Action<IEngineCommand> enqueueCommand,
        Action<EvalJob> enqueueEval,
        Action<string> cancelEval,
        Action<Guid, string> sendProtocolError,
        ReplConfig config,
        Func<int> queuedCommandCount,
        Action<string> log
    )
    {
        _enqueueCommand = enqueueCommand;
        _enqueueEval = enqueueEval;
        _cancelEval = cancelEval;
        _sendProtocolError = sendProtocolError;
        _config = config;
        _queuedCommandCount = queuedCommandCount;
        _log = log;
    }

    internal static MessageRouter CreateForTests(
        Action<IEngineCommand> enqueueCommand,
        Action<EvalJob> enqueueEval,
        Action<string> cancelEval,
        Action<Guid, string> sendProtocolError,
        ReplConfig config,
        Func<int> queuedCommandCount
    ) =>
        new(
            enqueueCommand,
            enqueueEval,
            cancelEval,
            sendProtocolError,
            config,
            queuedCommandCount,
            _ => { }
        );

    /// <summary>
    /// Routes one inbound frame. Called on a Fleck thread — must not block.
    /// </summary>
    public void HandleMessage(Guid connectionId, string rawJson)
    {
        if (RejectOversizedMessage(connectionId, rawJson))
            return;

        if (!TryParseType(connectionId, rawJson, out var type))
            return;

        if (RejectLegacyMessage(connectionId, rawJson, type))
            return;

        RouteParsedMessage(connectionId, rawJson, type);
    }

    private bool RejectOversizedMessage(Guid connectionId, string rawJson)
    {
        if (Encoding.UTF8.GetByteCount(rawJson) <= _config.MaxMessageBytes)
            return false;

        SendError(
            connectionId,
            id: null,
            ProtocolV2.ErrorKind.InvalidRequest,
            "messageTooLarge",
            "Message exceeds maxMessageBytes.",
            retryable: false
        );
        return true;
    }

    private bool TryParseType(Guid connectionId, string rawJson, out string type)
    {
        try
        {
            type = MessageSerializer.ParseType(rawJson);
            return true;
        }
        catch (Exception ex)
        {
            type = string.Empty;
            SendError(
                connectionId,
                id: null,
                ProtocolV2.ErrorKind.InvalidRequest,
                "invalidJson",
                ex.Message,
                retryable: false
            );
            return false;
        }
    }

    private bool RejectLegacyMessage(Guid connectionId, string rawJson, string type)
    {
        if (!IsLegacyMessageType(type))
            return false;

        SendError(
            connectionId,
            ExtractId(rawJson),
            ProtocolV2.ErrorKind.InvalidRequest,
            "legacyMessageType",
            $"Message type '{type}' is not supported by protocol v2.",
            retryable: false
        );
        return true;
    }

    private void RouteParsedMessage(Guid connectionId, string rawJson, string type)
    {
        try
        {
            EnqueueParsedCommand(connectionId, rawJson, BuildCommand(connectionId, rawJson, type));
        }
        catch (Exception ex)
        {
            _log($"[HotRepl] Failed to route message: {ex.Message}");
            SendError(
                connectionId,
                ExtractId(rawJson),
                ProtocolV2.ErrorKind.InvalidRequest,
                "invalidRequest",
                ex.Message,
                retryable: false
            );
        }
    }

    private void EnqueueParsedCommand(Guid connectionId, string rawJson, IEngineCommand? cmd)
    {
        if (cmd == null)
            return;

        if (_queuedCommandCount() >= _config.MaxQueuedCommands)
        {
            SendError(
                connectionId,
                ExtractId(rawJson),
                ProtocolV2.ErrorKind.Busy,
                "commandQueueFull",
                "Command queue is full.",
                retryable: true
            );
            return;
        }

        _enqueueCommand(cmd);
    }

    /// <summary>
    /// Returns the queued <see cref="IEngineCommand"/> for <paramref name="rawJson"/>,
    /// or <c>null</c> if the message was handled out-of-band (eval enqueue, cancel)
    /// or its type is unknown.
    /// </summary>
    private IEngineCommand? BuildCommand(Guid connectionId, string rawJson, string type)
    {
        switch (type)
        {
            case MessageType.Eval:
                if (_queuedCommandCount() >= _config.MaxQueuedCommands)
                {
                    SendError(
                        connectionId,
                        ExtractId(rawJson),
                        ProtocolV2.ErrorKind.Busy,
                        "commandQueueFull",
                        "Command queue is full.",
                        retryable: true
                    );
                    return null;
                }
                EnqueueEval(connectionId, rawJson);
                return null;
            case MessageType.Cancel:
                CancelEval(rawJson);
                return null;
            case MessageType.Reset:
                return new ResetCmd(De<ResetMessage>(rawJson).Id, connectionId);
            case MessageType.Complete:
                var cmpl = De<CompleteMessage>(rawJson);
                return new CompleteCmd(cmpl.Id, cmpl.Code, cmpl.CursorPos, connectionId);
            case MessageType.Subscribe:
                return BuildSubscribeCmd(connectionId, De<SubscribeMessage>(rawJson));
            case MessageType.SelectEvaluator:
                var sel = De<SelectEvaluatorMessage>(rawJson);
                return new SelectEvaluatorCmd(sel.Id, sel.Evaluator, connectionId);
            case MessageType.CommandDescribe:
                return new CommandDescribeCmd(De<CommandDescribeMessage>(rawJson).Id, connectionId);
            case MessageType.CommandCall:
                return new CommandCallCmd(De<CommandCallMessage>(rawJson), connectionId);
            case MessageType.JobStatus:
                return new JobStatusCmd(De<JobStatusMessage>(rawJson), connectionId);
            case MessageType.JobCancel:
                return new JobCancelCmd(De<JobCancelMessage>(rawJson), connectionId);
            default:
                SendError(
                    connectionId,
                    ExtractId(rawJson),
                    ProtocolV2.ErrorKind.InvalidRequest,
                    "unknownMessageType",
                    $"Unknown message type '{type}'.",
                    retryable: false
                );
                return null;
        }
    }

    private void EnqueueEval(Guid connectionId, string rawJson)
    {
        var msg = De<EvalMessage>(rawJson);
        var timeoutMs = msg.TimeoutMs > 0 ? msg.TimeoutMs : _config.DefaultTimeoutMs;
        _enqueueEval(new EvalJob(msg.Id, msg.Code, timeoutMs, connectionId));
    }

    private void CancelEval(string rawJson)
    {
        // Cancel is time-sensitive: skip the queue and abort directly.
        _cancelEval(De<CancelMessage>(rawJson).Id);
    }

    private SubscribeCmd BuildSubscribeCmd(Guid connectionId, SubscribeMessage msg) =>
        new(
            msg.Id,
            msg.Code,
            Math.Max(1, msg.IntervalFrames),
            msg.OnChange,
            msg.Limit,
            msg.TimeoutMs > 0 ? msg.TimeoutMs : _config.DefaultTimeoutMs,
            connectionId
        );

    private static bool IsLegacyMessageType(string type) =>
        string.Equals(type, MessageType.ControlAuth, StringComparison.Ordinal)
        || string.Equals(type, MessageType.LeaseAcquire, StringComparison.Ordinal)
        || string.Equals(type, MessageType.Ping, StringComparison.Ordinal)
        || string.Equals(type, MessageType.JobResult, StringComparison.Ordinal);

    private static string? ExtractId(string rawJson)
    {
        try
        {
            return MessageSerializer.ParseId(rawJson);
        }
        catch
        {
            return null;
        }
    }

    private void SendError(
        Guid connectionId,
        string? id,
        string kind,
        string code,
        string message,
        bool retryable
    )
    {
        var json = ProtocolMessageSerializer.Serialize(
            new
            {
                type = "error",
                id,
                error = new ProtocolV2.HotReplErrorEnvelope(
                    kind,
                    code,
                    message,
                    retryable,
                    details: null
                ),
            }
        );
        _sendProtocolError(connectionId, json);
    }

    private static T De<T>(string rawJson) => MessageSerializer.Deserialize<T>(rawJson);
}
