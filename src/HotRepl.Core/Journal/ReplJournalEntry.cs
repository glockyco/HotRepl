using System;

namespace HotRepl.Journal;

/// <summary>Metadata-only server-side journal entry.</summary>
internal sealed record ReplJournalEntry(
    string Id,
    string Kind,
    string? Name,
    bool Success,
    long DurationMs,
    string? ErrorKind,
    DateTimeOffset Timestamp
);
