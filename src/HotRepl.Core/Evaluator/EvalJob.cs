using System;

namespace HotRepl.Evaluator;

/// <summary>
/// An evaluation request dequeued by Tick() from the eval queue.
/// Immutable value — created on Fleck thread, consumed on main thread.
/// </summary>
internal sealed class EvalJob
{
    public string Id { get; }
    public string Code { get; }
    public int TimeoutMs { get; }

    /// <summary>
    /// The Fleck connection ID of the client that submitted this eval.
    /// Used to route the result back to the right socket.
    /// </summary>
    public Guid ConnectionId { get; }

    public EvalJob(string id, string code, int timeoutMs, Guid connectionId)
    {
        Id = id;
        Code = code;
        TimeoutMs = timeoutMs;
        ConnectionId = connectionId;
    }
}
