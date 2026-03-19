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

    private readonly Dictionary<string, SubscriptionState> _subscriptions = new();
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
            new List<SubscriptionState>(_subscriptions.Values));

    // ── Per-frame processing ──────────────────────────────────────────────────

    /// <summary>
    /// Evaluates all due subscriptions.
    /// <paramref name="guardedEvaluate"/> is the engine's watchdog-wrapped eval delegate;
    /// <paramref name="send"/> routes a serialized response back to the right client.
    /// </summary>
    public void Tick(
        Func<string, string, int, EvalOutcome> guardedEvaluate,
        Action<Guid, string> send,
        IResultSerializer serializer)
    {
        if (_subscriptions.Count == 0)
            return;

        List<string>? toRemove = null;

        foreach (var kvp in _subscriptions)
        {
            var sub = kvp.Value;
            if (!sub.Active)
                continue;

            sub.FramesSinceLast++;
            if (sub.FramesSinceLast < sub.IntervalFrames)
                continue;
            sub.FramesSinceLast = 0;

            var outcome = guardedEvaluate(sub.Id, sub.Code, sub.TimeoutMs);

            if (outcome.Success)
            {
                sub.ConsecutiveErrors = 0;

                string? serialized = null;
                if (outcome.HasValue && outcome.Value != null)
                {
                    serialized = serializer.Serialize(outcome.Value, _config);
                    serialized = serializer.Truncate(serialized, _config.MaxResultLength);
                }

                // onChange: suppress if value hasn't changed since last delivery.
                if (sub.OnChange && serialized == sub.LastValue)
                    continue;

                sub.LastValue = serialized;
                sub.Seq++;
                sub.DeliveryCount++;
                bool isFinal = sub.Limit > 0 && sub.DeliveryCount >= sub.Limit;

                send(sub.ConnectionId, MessageSerializer.Serialize(new SubscribeResultMessage
                {
                    Id = sub.Id,
                    Seq = sub.Seq,
                    HasValue = outcome.HasValue,
                    Value = serialized,
                    ValueType = outcome.ValueType,
                    DurationMs = outcome.DurationMs,
                    Final = isFinal,
                }));

                if (isFinal)
                    (toRemove ??= new List<string>()).Add(sub.Id);
            }
            else
            {
                sub.Seq++;
                sub.ConsecutiveErrors++;
                bool isFinal = sub.ConsecutiveErrors >= MaxConsecutiveErrors;

                send(sub.ConnectionId, MessageSerializer.Serialize(new SubscribeErrorMessage
                {
                    Id = sub.Id,
                    Seq = sub.Seq,
                    ErrorKind = outcome.ErrorKind ?? Protocol.ErrorKind.Runtime,
                    Message = outcome.ErrorMessage ?? "Unknown error",
                    Final = isFinal,
                }));

                if (isFinal)
                    (toRemove ??= new List<string>()).Add(sub.Id);
            }
        }

        if (toRemove != null)
            foreach (var id in toRemove)
                _subscriptions.Remove(id);
    }
}
