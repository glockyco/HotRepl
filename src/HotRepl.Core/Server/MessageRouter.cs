using System;
using HotRepl.Engine.Commands;
using HotRepl.Evaluator;
using HotRepl.Protocol;

namespace HotRepl.Server;

/// <summary>
/// Translates inbound JSON frames into typed engine commands and enqueues them.
/// Cancel is the only message that bypasses the queue (it must pre-empt running evals).
/// All other messages are queued for Tick() to process on the main thread.
/// </summary>
internal sealed class MessageRouter
{
    private readonly ReplEngine _engine;
    private readonly Action<string> _log;

    public MessageRouter(ReplEngine engine, Action<string> log)
    {
        _engine = engine;
        _log = log;
    }

    /// <summary>
    /// Routes one inbound frame. Called on a Fleck thread — must not block.
    /// </summary>
    public void HandleMessage(Guid connectionId, string rawJson)
    {
        try
        {
            var cmd = BuildCommand(connectionId, rawJson);
            if (cmd != null)
                _engine.EnqueueCommand(cmd);
        }
        catch (Exception ex)
        {
            _log($"[HotRepl] Failed to route message: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the queued <see cref="IEngineCommand"/> for <paramref name="rawJson"/>,
    /// or <c>null</c> if the message was handled out-of-band (eval enqueue, cancel)
    /// or its type is unknown.
    /// </summary>
    private IEngineCommand? BuildCommand(Guid connectionId, string rawJson)
    {
        var type = MessageSerializer.ParseType(rawJson);
        switch (type)
        {
            case MessageType.Eval:
                EnqueueEval(connectionId, rawJson);
                return null;
            case MessageType.Cancel:
                CancelEval(rawJson);
                return null;
            case MessageType.Reset:
                return new ResetCmd(De<ResetMessage>(rawJson).Id, connectionId);
            case MessageType.Ping:
                return new PingCmd(De<PingMessage>(rawJson).Id, connectionId);
            case MessageType.Complete:
                var cmpl = De<CompleteMessage>(rawJson);
                return new CompleteCmd(cmpl.Id, cmpl.Code, cmpl.CursorPos, connectionId);
            case MessageType.Subscribe:
                return BuildSubscribeCmd(connectionId, De<SubscribeMessage>(rawJson));
            case MessageType.SelectEvaluator:
                var sel = De<SelectEvaluatorMessage>(rawJson);
                return new SelectEvaluatorCmd(sel.Id, sel.Evaluator, connectionId);
            case MessageType.ControlAuth:
                var auth = De<ControlAuthMessage>(rawJson);
                return new ControlAuthCmd(auth.Id, auth.Token, connectionId);
            case MessageType.LeaseAcquire:
                var la = De<LeaseAcquireMessage>(rawJson);
                return new LeaseAcquireCmd(la.Id, la.SessionId, la.ClientName, connectionId);
            case MessageType.CommandDescribe:
                return new CommandDescribeCmd(De<CommandDescribeMessage>(rawJson).Id, connectionId);
            case MessageType.CommandCall:
                return new CommandCallCmd(De<CommandCallMessage>(rawJson), connectionId);
            case MessageType.JobStatus:
                return new JobStatusCmd(De<JobStatusMessage>(rawJson), connectionId);
            case MessageType.JobResult:
                return new JobResultCmd(De<JobResultRequestMessage>(rawJson), connectionId);
            case MessageType.JobCancel:
                return new JobCancelCmd(De<JobCancelMessage>(rawJson), connectionId);
            default:
                _log($"[HotRepl] Unknown message type '{type}' — ignored.");
                return null;
        }
    }

    private void EnqueueEval(Guid connectionId, string rawJson)
    {
        var msg = De<EvalMessage>(rawJson);
        var timeoutMs = msg.TimeoutMs > 0 ? msg.TimeoutMs : _engine.Config.DefaultTimeoutMs;
        _engine.EnqueueEval(new EvalJob(msg.Id, msg.Code, timeoutMs, connectionId));
    }

    private void CancelEval(string rawJson)
    {
        // Cancel is time-sensitive: skip the queue and abort directly.
        _engine.CancelEval(De<CancelMessage>(rawJson).Id);
    }

    private SubscribeCmd BuildSubscribeCmd(Guid connectionId, SubscribeMessage msg) =>
        new(
            msg.Id,
            msg.Code,
            Math.Max(1, msg.IntervalFrames),
            msg.OnChange,
            msg.Limit,
            msg.TimeoutMs > 0 ? msg.TimeoutMs : _engine.Config.DefaultTimeoutMs,
            connectionId
        );

    private static T De<T>(string rawJson) => MessageSerializer.Deserialize<T>(rawJson);
}
