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
    private readonly Queue<ReplJournalEntry> _entries;
    private readonly object _gate = new();
    private long _resetCount;

    public ReplJournal(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");

        _capacity = capacity;
        _entries = new Queue<ReplJournalEntry>(capacity);
    }

    public void RecordEval(string id, bool success, long durationMs, string? errorKind) =>
        Record(new ReplJournalEntry(id, EvalKind, null, success, durationMs, errorKind, DateTimeOffset.UtcNow));

    public void RecordCommand(
        string id,
        string name,
        bool success,
        long durationMs,
        string? errorKind
    ) =>
        Record(new ReplJournalEntry(id, CommandKind, name, success, durationMs, errorKind, DateTimeOffset.UtcNow));

    public void RecordReset(string id)
    {
        _ = id;
        lock (_gate)
            _resetCount++;
    }

    public IReadOnlyList<ReplJournalEntry> Query(string? kind, int limit)
    {
        if (limit <= 0)
            return Array.Empty<ReplJournalEntry>();

        lock (_gate)
        {
            IEnumerable<ReplJournalEntry> query = _entries;
            if (!string.IsNullOrEmpty(kind))
                query = query.Where(entry => string.Equals(entry.Kind, kind, StringComparison.Ordinal));

            return query.TakeLast(limit).ToArray();
        }
    }

    private void Record(ReplJournalEntry entry)
    {
        lock (_gate)
        {
            if (_entries.Count == _capacity)
                _entries.Dequeue();
            _entries.Enqueue(entry);
        }
    }
}
