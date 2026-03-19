using System;
using HotRepl.Evaluator;
using HotRepl.Protocol;

namespace HotRepl.Server;

/// <summary>
/// Deserializes inbound JSON frames on Fleck thread-pool threads and routes them
/// to the appropriate engine entry point.
///
/// Cancel messages call CancelEval() directly — they bypass the command queue
/// for immediate abort. All other messages are enqueued for main-thread processing.
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
            var type = MessageSerializer.ParseType(rawJson);
            switch (type)
            {
                case MessageType.Eval:
                    {
                        var msg = MessageSerializer.Deserialize<EvalMessage>(rawJson);
                        var timeoutMs = msg.TimeoutMs > 0 ? msg.TimeoutMs : _engine.Config.DefaultTimeoutMs;
                        _engine.EnqueueEval(new EvalJob(msg.Id, msg.Code, timeoutMs, connectionId));
                        break;
                    }
                case MessageType.Cancel:
                    {
                        // Cancel is time-sensitive: skip the queue and abort directly.
                        var msg = MessageSerializer.Deserialize<CancelMessage>(rawJson);
                        _engine.CancelEval(msg.Id);
                        break;
                    }
                case MessageType.Reset:
                    {
                        var msg = MessageSerializer.Deserialize<ResetMessage>(rawJson);
                        _engine.EnqueueCommand(new ResetCmd(msg.Id, connectionId));
                        break;
                    }
                case MessageType.Ping:
                    {
                        var msg = MessageSerializer.Deserialize<PingMessage>(rawJson);
                        _engine.EnqueueCommand(new PingCmd(msg.Id, connectionId));
                        break;
                    }
                case MessageType.Complete:
                    {
                        var msg = MessageSerializer.Deserialize<CompleteMessage>(rawJson);
                        _engine.EnqueueCommand(new CompleteCmd(msg.Id, msg.Code, msg.CursorPos, connectionId));
                        break;
                    }
                case MessageType.Subscribe:
                    {
                        var msg = MessageSerializer.Deserialize<SubscribeMessage>(rawJson);
                        _engine.EnqueueCommand(new SubscribeCmd(
                            msg.Id, msg.Code,
                            Math.Max(1, msg.IntervalFrames),
                            msg.OnChange,
                            msg.Limit,
                            msg.TimeoutMs > 0 ? msg.TimeoutMs : _engine.Config.DefaultTimeoutMs,
                            connectionId));
                        break;
                    }
                default:
                    _log($"[HotRepl] Unknown message type '{type}' — ignored.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log($"[HotRepl] Failed to route message: {ex.Message}");
        }
    }
}
