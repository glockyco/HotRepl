using System;

namespace HotRepl.Helpers;

internal sealed record HistoryEntry(string Code, string? Value, string? Error)
{
    public string Timestamp { get; } = DateTime.UtcNow.ToString("o");
}
