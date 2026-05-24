using System;
using System.Collections.Generic;
using System.Linq;

namespace HotRepl.Journal;

/// <summary>Server-owned ring-buffer journal for recent eval and command metadata.</summary>
internal sealed class ReplJournal
{
    private const string EvalKind = "eval";
    private const string CommandKind = "command";

    private readonly int _capacity;
    private readonly Queue<ReplJournalEntry> _evalEntries;
    private readonly Queue<ReplJournalEntry> _commandEntries;
    private readonly object _gate = new();
    private long _sequence;

    public ReplJournal(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Capacity must be positive."
            );

        _capacity = capacity;
        _evalEntries = new Queue<ReplJournalEntry>(capacity);
        _commandEntries = new Queue<ReplJournalEntry>(capacity);
    }

    public void RecordEval(string id, bool success, long durationMs, string? errorKind) =>
        Record(id, EvalKind, name: null, success, durationMs, errorKind);

    public void RecordCommand(
        string id,
        string name,
        bool success,
        long durationMs,
        string? errorKind
    ) => Record(id, CommandKind, name, success, durationMs, errorKind);

    public void RecordReset(string id) =>
        Record(id, CommandKind, name: "reset", success: true, durationMs: 0, errorKind: null);

    public IReadOnlyList<ReplJournalEntry> Query(string? kind, int limit)
    {
        if (limit <= 0)
            return Array.Empty<ReplJournalEntry>();

        lock (_gate)
        {
            if (string.Equals(kind, EvalKind, StringComparison.Ordinal))
                return TakeLast(_evalEntries, limit);
            if (string.Equals(kind, CommandKind, StringComparison.Ordinal))
                return TakeLast(_commandEntries, limit);
            if (!string.IsNullOrEmpty(kind))
                return Array.Empty<ReplJournalEntry>();

            var combined = _evalEntries
                .Concat(_commandEntries)
                .OrderBy(entry => entry.Sequence)
                .ToArray();
            return combined.Skip(Math.Max(0, combined.Length - limit)).ToArray();
        }
    }

    private static ReplJournalEntry[] TakeLast(Queue<ReplJournalEntry> entries, int limit) =>
        entries.Skip(Math.Max(0, entries.Count - limit)).ToArray();

    private void Record(
        string id,
        string kind,
        string? name,
        bool success,
        long durationMs,
        string? errorKind
    )
    {
        lock (_gate)
        {
            var entry = new ReplJournalEntry(
                id,
                kind,
                name,
                success,
                durationMs,
                errorKind,
                DateTimeOffset.UtcNow,
                _sequence++
            );
            var entries = string.Equals(kind, EvalKind, StringComparison.Ordinal)
                ? _evalEntries
                : _commandEntries;
            if (entries.Count == _capacity)
                entries.Dequeue();
            entries.Enqueue(entry);
        }
    }
}
