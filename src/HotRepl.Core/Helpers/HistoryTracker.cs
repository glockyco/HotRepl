using System;
using System.Collections.Generic;

namespace HotRepl.Helpers;

/// <summary>
/// Thread-unsafe ring buffer of eval history entries.
/// All access is from the main thread (inside Tick()) — no synchronization needed.
/// </summary>
internal sealed class HistoryTracker
{
    private const int Capacity = 100;
    private readonly List<HistoryEntry> _entries = new(Capacity);

    /// <summary>Records one eval result. Oldest entry is evicted when capacity is exceeded.</summary>
    public void RecordEntry(string code, string? value, string? error)
    {
        if (_entries.Count >= Capacity)
            _entries.RemoveAt(0);

        _entries.Add(new HistoryEntry(code, value, error));
    }

    /// <summary>Returns the most recent <paramref name="limit"/> entries, oldest first.</summary>
    public HistoryEntry[] GetRecent(int limit)
    {
        if (limit <= 0)
            return Array.Empty<HistoryEntry>();
        int start = Math.Max(0, _entries.Count - limit);
        int count = _entries.Count - start;
        var result = new HistoryEntry[count];
        _entries.CopyTo(start, result, 0, count);
        return result;
    }
}

internal sealed class HistoryEntry
{
    public string Code { get; }
    public string? Value { get; }
    public string? Error { get; }
    public string Timestamp { get; }

    public HistoryEntry(string code, string? value, string? error)
    {
        Code = code;
        Value = value;
        Error = error;
        Timestamp = DateTime.UtcNow.ToString("o");
    }
}
