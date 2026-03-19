using System;

namespace HotRepl.Evaluator;

/// <summary>Result of an autocomplete request. Never throws — empty array on failure.</summary>
internal sealed class CompletionResult
{
    public string[] Completions { get; }
    public long DurationMs { get; }

    public CompletionResult(string[] completions, long durationMs)
    {
        Completions = completions ?? Array.Empty<string>();
        DurationMs = durationMs;
    }
}
