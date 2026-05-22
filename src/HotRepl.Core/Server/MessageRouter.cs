using System;
using System.Text;
using HotRepl.Engine.Commands;
using HotRepl.Evaluator;
using HotRepl.Protocol;
using HotRepl.Protocol.Serialization;
using Newtonsoft.Json.Linq;

namespace HotRepl.Server;

/// <summary>
/// Translates inbound JSON frames into typed engine commands and enqueues them.
/// Cancel is the only message that bypasses the queue (it must pre-empt running evals).
/// All other messages are queued for Tick() to process on the main thread.
/// </summary>
internal sealed class MessageRouter
{
    private const string ControlAuthMessageType = "control_auth";
    private const string LeaseAcquireMessageType = "lease_acquire";
    private const string PingMessageType = "ping";
    private const string SelectEvaluatorMessageType = "select_evaluator";

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
            ErrorKind.InvalidRequest,
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
            type = ProtocolMessageSerializer.ParseType(rawJson);
            return true;
        }
        catch (Exception ex)
        {
            type = string.Empty;
            SendError(
                connectionId,
                id: null,
                ErrorKind.InvalidRequest,
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
            ErrorKind.InvalidRequest,
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
                ErrorKind.InvalidRequest,
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
                ErrorKind.Busy,
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
                        ErrorKind.Busy,
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
                return new CompleteCmd(
                    cmpl.Id,
                    cmpl.Code,
                    cmpl.Cursor.GetValueOrDefault(cmpl.Code.Length),
                    connectionId
                );
            case MessageType.Subscribe:
                return BuildSubscribeCmd(connectionId, De<SubscribeMessage>(rawJson));
            case SelectEvaluatorMessageType:
                var sel = ParseSelectEvaluator(rawJson);
                return new SelectEvaluatorCmd(sel.Id, sel.Evaluator, connectionId);
            case MessageType.CommandsList:
                var list = De<CommandsListMessage>(rawJson);
                return new CommandsListCmd(list.Id, list.Since, connectionId);
            case MessageType.CommandDescribe:
                var describe = De<CommandDescribeMessage>(rawJson);
                return new CommandDescribeCmd(describe.Id, describe.Name, connectionId);
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
                    ErrorKind.InvalidRequest,
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
        var requestedTimeout = msg.TimeoutMs.GetValueOrDefault();
        var timeoutMs = requestedTimeout > 0 ? requestedTimeout : _config.DefaultTimeoutMs;
        _enqueueEval(new EvalJob(msg.Id, msg.Code, timeoutMs, connectionId));
    }

    private void CancelEval(string rawJson)
    {
        // Cancel is time-sensitive: skip the queue and abort directly.
        _cancelEval(De<CancelMessage>(rawJson).TargetId);
    }

    private SubscribeCmd BuildSubscribeCmd(Guid connectionId, SubscribeMessage msg) =>
        new(
            msg.Id,
            msg.Code,
            Math.Max(1, msg.IntervalFrames.GetValueOrDefault(1)),
            msg.OnChange.GetValueOrDefault(),
            msg.Limit.GetValueOrDefault(),
            RequestedTimeoutOrDefault(msg.TimeoutMs),
            connectionId
        );

    private int RequestedTimeoutOrDefault(int? timeoutMs)
    {
        var requested = timeoutMs.GetValueOrDefault();
        return requested > 0 ? requested : _config.DefaultTimeoutMs;
    }


    private static (string Id, string Evaluator) ParseSelectEvaluator(string rawJson)
    {
        var obj = JObject.Parse(rawJson);
        return (
            obj["id"]?.Value<string>() ?? string.Empty,
            obj["evaluator"]?.Value<string>() ?? string.Empty
        );
    }
    private static bool IsLegacyMessageType(string type) =>
        string.Equals(type, ControlAuthMessageType, StringComparison.Ordinal)
        || string.Equals(type, LeaseAcquireMessageType, StringComparison.Ordinal)
        || string.Equals(type, PingMessageType, StringComparison.Ordinal)
        || string.Equals(type, MessageType.JobResult, StringComparison.Ordinal);

    private static string? ExtractId(string rawJson)
    {
        try
        {
            return ProtocolMessageSerializer.ParseId(rawJson);
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
                error = new HotReplErrorEnvelope(kind, code, message, retryable, details: null),
            }
        );
        _sendProtocolError(connectionId, json);
    }

    private static T De<T>(string rawJson) => ProtocolMessageSerializer.Deserialize<T>(rawJson);
}
