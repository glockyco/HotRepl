using System;
using System.Collections.Generic;
using HotRepl.Evaluator;
using HotRepl.Protocol;
using HotRepl.Serialization;

namespace HotRepl.Subscriptions;

/// <summary>
/// Manages up to <see cref="MaxSubscriptions"/> recurring expression subscriptions.
/// All operations happen on the main thread inside Tick().
///
/// Subscriptions share the same evaluator as regular evals — they observe live
/// session state and are affected by resets. The watchdog/abort mechanism is
/// provided by the engine via a delegate, so subscription evals participate in
/// the same cancel/timeout flow as regular evals.
/// </summary>
internal sealed class SubscriptionManager
{
    private const int MaxSubscriptions = 8;
    private const int MaxConsecutiveErrors = 3;

    private readonly Dictionary<string, SubscriptionState> _subscriptions = new(
        StringComparer.Ordinal
    );
    private readonly ReplConfig _config;

    public SubscriptionManager(ReplConfig config) => _config = config;

    public int Count => _subscriptions.Count;

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to register a new subscription.
    /// Returns false and populates <paramref name="errorMessage"/> if the limit is reached.
    /// </summary>
    public bool TryAdd(SubscriptionState sub, out string? errorMessage)
    {
        if (_subscriptions.Count >= MaxSubscriptions)
        {
            errorMessage = $"Maximum {MaxSubscriptions} active subscriptions reached.";
            return false;
        }
        _subscriptions[sub.Id] = sub;
        errorMessage = null;
        return true;
    }

    public void Cancel(string id) => _subscriptions.Remove(id);

    public void CancelAll() => _subscriptions.Clear();

    /// <summary>Snapshot of all active subscriptions. Used by reset to notify before clearing.</summary>
    public IReadOnlyCollection<SubscriptionState> GetAll() =>
        new System.Collections.ObjectModel.ReadOnlyCollection<SubscriptionState>(
            new List<SubscriptionState>(_subscriptions.Values)
        );

    // ── Per-frame processing ──────────────────────────────────────────────────

    /// <summary>
    /// Evaluates all due subscriptions.
    /// <paramref name="guardedEvaluate"/> is the engine's watchdog-wrapped eval delegate;
    /// <paramref name="send"/> routes a serialized response back to the right client.
    /// </summary>
    public void Tick(
        Func<string, string, int, EvalOutcome> guardedEvaluate,
        Action<Guid, string> send
    )
    {
        if (_subscriptions.Count == 0)
            return;

        List<string>? toRemove = null;

        foreach (var kvp in _subscriptions)
        {
            if (!ShouldRunThisTick(kvp.Value))
                continue;
            if (TickOne(kvp.Value, guardedEvaluate, send))
                (toRemove ??= new List<string>()).Add(kvp.Value.Id);
        }

        if (toRemove != null)
            foreach (var id in toRemove)
                _subscriptions.Remove(id);
    }

    private static bool ShouldRunThisTick(SubscriptionState sub)
    {
        if (!sub.Active)
            return false;
        sub.FramesSinceLast++;
        if (sub.FramesSinceLast < sub.IntervalFrames)
            return false;
        sub.FramesSinceLast = 0;
        return true;
    }

    /// <summary>Returns true when the subscription has terminated and should be removed.</summary>
    private bool TickOne(
        SubscriptionState sub,
        Func<string, string, int, EvalOutcome> guardedEvaluate,
        Action<Guid, string> send
    )
    {
        var outcome = guardedEvaluate(sub.Id, sub.Code, sub.TimeoutMs);
        return outcome.Success
            ? DeliverValue(sub, outcome, send)
            : DeliverError(sub, outcome, send);
    }

    private bool DeliverValue(SubscriptionState sub, EvalOutcome outcome, Action<Guid, string> send)
    {
        sub.ConsecutiveErrors = 0;

        string? serialized = null;
        if (outcome.HasValue && outcome.Value != null)
        {
            serialized = JsonResultSerializer.Serialize(outcome.Value, _config);
            serialized = JsonResultSerializer.Truncate(serialized, _config.MaxResultLength);
        }

        // onChange: suppress if value hasn't changed since last delivery.
        if (sub.OnChange && string.Equals(serialized, sub.LastValue, StringComparison.Ordinal))
            return false;

        sub.LastValue = serialized;
        sub.Seq++;
        sub.DeliveryCount++;
        bool isFinal = sub.Limit > 0 && sub.DeliveryCount >= sub.Limit;

        send(
            sub.ConnectionId,
            MessageSerializer.Serialize(
                new SubscribeResultMessage
                {
                    Id = sub.Id,
                    Seq = sub.Seq,
                    HasValue = outcome.HasValue,
                    Value = serialized,
                    ValueType = outcome.ValueType,
                    DurationMs = outcome.DurationMs,
                    Final = isFinal,
                }
            )
        );

        return isFinal;
    }

    private static bool DeliverError(
        SubscriptionState sub,
        EvalOutcome outcome,
        Action<Guid, string> send
    )
    {
        sub.Seq++;
        sub.ConsecutiveErrors++;
        bool isFinal = sub.ConsecutiveErrors >= MaxConsecutiveErrors;

        send(
            sub.ConnectionId,
            MessageSerializer.Serialize(
                new SubscribeErrorMessage
                {
                    Id = sub.Id,
                    Seq = sub.Seq,
                    ErrorKind = outcome.ErrorKind ?? Protocol.ErrorKind.Runtime,
                    Message = outcome.ErrorMessage ?? "Unknown error",
                    Final = isFinal,
                }
            )
        );

        return isFinal;
    }
}
