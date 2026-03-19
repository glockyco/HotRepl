using System;

namespace HotRepl.Subscriptions;

/// <summary>
/// Mutable per-subscription state. Accessed exclusively from the main thread
/// inside SubscriptionManager.Tick() — no synchronization needed.
/// </summary>
internal sealed class SubscriptionState
{
    public string Id { get; }
    public Guid ConnectionId { get; }
    public string Code { get; }
    public int IntervalFrames { get; }
    public bool OnChange { get; }
    public int Limit { get; }         // 0 = unlimited
    public int TimeoutMs { get; }

    // ── Mutable fields updated each tick ──────────────────────────────────────
    public int Seq { get; set; }
    public int FramesSinceLast { get; set; }
    public int DeliveryCount { get; set; }
    public int ConsecutiveErrors { get; set; }
    public string? LastValue { get; set; }
    public bool Active { get; set; } = true;

    public SubscriptionState(
        string id, Guid connectionId, string code,
        int intervalFrames, bool onChange, int limit, int timeoutMs)
    {
        Id = id;
        ConnectionId = connectionId;
        Code = code;
        IntervalFrames = intervalFrames;
        OnChange = onChange;
        Limit = limit;
        TimeoutMs = timeoutMs;
    }
}
