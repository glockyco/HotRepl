using System;
using System.Linq;
using BepInEx.Logging;
using HotRepl.Evaluator;

namespace HotRepl.BepInEx;

/// <summary>
/// Collects console output under BepInEx 5. With the runtime patches enabled, which is the default,
/// <see cref="Console.SetOut"/> no longer replaces <see cref="Console.Out"/>: BepInEx keeps its own
/// writer as the console writer, logs every string written to it through the <c>Console</c> log
/// source, and forwards it to the writer passed to SetOut. Replacing the console writer is therefore
/// impossible, and the capture listens to that log source instead.
/// </summary>
/// <remarks>
/// BepInEx logs each string write as one entry and does not log character writes, so the collected
/// text holds one line per string write.
/// </remarks>
internal sealed class BepInExConsoleCapture : ConsoleCapture, ILogListener
{
    private const string ConsoleSourceName = "Console";

    private readonly ILogSource _consoleSource;

    private BepInExConsoleCapture(ILogSource consoleSource)
    {
        _consoleSource = consoleSource;
        Logger.Listeners.Add(this);
    }

    /// <summary>
    /// Observes BepInEx's console log source when the runtime patches created it. Without the
    /// patches, SetOut replaces the console writer and the capture redirects it instead.
    /// </summary>
    public static ConsoleCapture Create()
    {
        var consoleSource = Logger.Sources.FirstOrDefault(source =>
            string.Equals(source.SourceName, ConsoleSourceName, StringComparison.Ordinal)
        );
        return consoleSource != null
            ? new BepInExConsoleCapture(consoleSource)
            : new ConsoleRedirectCapture();
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (IsCollecting && ReferenceEquals(eventArgs.Source, _consoleSource))
            Collect(string.Concat(eventArgs.Data?.ToString(), Environment.NewLine));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Logger.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}
