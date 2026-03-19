using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Evaluator;

/// <summary>
/// Captures stdout produced by evaluated code without swapping Console.Out globally.
///
/// A TeeWriter is installed once at startup and permanently wraps BepInEx's Console.Out
/// sink as its primary. All writes always flow to the primary (BepInEx logging stays intact).
/// Additionally, if the calling thread has an active capture buffer ([ThreadStatic]),
/// writes are teed there too.
///
/// Since all evaluation runs on the Unity main thread, [ThreadStatic] provides perfect
/// isolation: background Fleck/timer threads have a null buffer and their writes flow
/// to BepInEx only — they never pollute an eval transcript.
/// </summary>
internal static class StdoutCapture
{
    // One buffer slot per thread. Only the main thread ever sets this non-null.
    [ThreadStatic]
    private static StringWriter? _captureBuffer;

    private static volatile bool _installed;

    /// <summary>
    /// Installs the TeeWriter. Called once from Awake() before any eval or connection.
    /// Idempotent — safe to call more than once (additional calls are no-ops).
    /// </summary>
    public static void Install()
    {
        if (_installed)
            return;
        Console.SetOut(new TeeWriter(Console.Out));
        _installed = true;
    }

    /// <summary>Activates capture on the calling thread. Must be balanced by EndCapture().</summary>
    public static void BeginCapture() => _captureBuffer = new StringWriter();

    /// <summary>
    /// Deactivates capture and returns everything written to stdout on this thread since
    /// BeginCapture(). Returns empty string when called without a prior BeginCapture().
    /// </summary>
    public static string EndCapture()
    {
        var result = _captureBuffer?.ToString() ?? string.Empty;
        _captureBuffer?.Dispose();
        _captureBuffer = null;
        return result;
    }

    // ── TeeWriter ────────────────────────────────────────────────────────────

    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _primary;

        public TeeWriter(TextWriter primary) => _primary = primary;

        public override Encoding Encoding => _primary.Encoding;

        // All overloads tee to the thread-local capture buffer if one is active.
        // We override every meaningful overload so Mono doesn't funnel everything
        // through Write(char) at character-by-character cost.

        public override void Write(char value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(char[]? buffer) { _primary.Write(buffer); _captureBuffer?.Write(buffer); }
        public override void Write(string? value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(bool value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(int value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(long value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(float value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(double value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(object? value) { _primary.Write(value); _captureBuffer?.Write(value); }
        public override void Write(char[] buffer, int index, int count) { _primary.Write(buffer, index, count); _captureBuffer?.Write(buffer, index, count); }

        public override void WriteLine() { _primary.WriteLine(); _captureBuffer?.WriteLine(); }
        public override void WriteLine(char value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }
        public override void WriteLine(char[]? buffer) { _primary.WriteLine(buffer); _captureBuffer?.WriteLine(buffer); }
        public override void WriteLine(string? value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }
        public override void WriteLine(bool value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }
        public override void WriteLine(int value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }
        public override void WriteLine(long value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }
        public override void WriteLine(float value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }
        public override void WriteLine(double value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }
        public override void WriteLine(object? value) { _primary.WriteLine(value); _captureBuffer?.WriteLine(value); }

        public override Task WriteAsync(char value) { _captureBuffer?.Write(value); return _primary.WriteAsync(value); }
        public override Task WriteAsync(string? value) { _captureBuffer?.Write(value); return _primary.WriteAsync(value); }
        public override Task WriteLineAsync() { _captureBuffer?.WriteLine(); return _primary.WriteLineAsync(); }
        public override Task WriteLineAsync(string? value) { _captureBuffer?.WriteLine(value); return _primary.WriteLineAsync(value); }

        public override void Flush() => _primary.Flush();
        public override Task FlushAsync() => _primary.FlushAsync();

        protected override void Dispose(bool disposing)
        {
            // Never dispose the primary — BepInEx owns it.
            base.Dispose(disposing);
        }
    }
}
