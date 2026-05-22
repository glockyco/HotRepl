namespace HotRepl.Control;

internal sealed record ControlCommandJournalEntry(
    string Id,
    string Name,
    bool Success,
    string? ErrorKind
);
