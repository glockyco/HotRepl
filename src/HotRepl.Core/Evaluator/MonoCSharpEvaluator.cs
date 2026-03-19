using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Mono.CSharp;

namespace HotRepl.Evaluator;

/// <summary>
/// Wraps Mono.CSharp.Evaluator to compile and execute C# 7.x code at runtime.
///
/// Threading: all public methods MUST be called from the Unity main thread.
/// Evaluate() catches ThreadAbortException internally when the engine's watchdog fires,
/// calls ResetAbort(), and returns an Aborted sentinel for RunGuarded to resolve.
/// </summary>
internal sealed class MonoCSharpEvaluator : ICodeEvaluator, IDisposable
{
    // Assemblies we never want to reference: mcs autocomplete artifacts and
    // stdlib duplicates that the Mono evaluator already loads implicitly.

    // Default namespaces opened in every evaluator session.
    // Exposed internally so ReplEngine can include them in the handshake defaultUsings[].
    internal static readonly string[] DefaultUsings =
    {
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Linq",
        "System.Reflection",
        "UnityEngine",
        "UnityEngine.SceneManagement",
    };

    private readonly IReplHost _host;
    private Mono.CSharp.Evaluator? _evaluator;
    private StringBuilder? _errors;
    private bool _isInitialized;
    private bool _disposed;

    // Tracks how many levels deep we are inside a Mono evaluator call.
    // AssemblyLoad events fire synchronously on the same thread. If we're
    // already inside Compile() or ReferenceAssembly(), a re-entrant call to
    // ReferenceAssembly() in OnAssemblyLoad would cause the evaluator to
    // regenerate its internal class, re-fire AssemblyLoad, and loop forever.
    // Blocking re-entry when depth > 0 breaks the cycle.
    private int _evaluatorCallDepth;

    public MonoCSharpEvaluator(IReplHost host)
    {
        _host = host;
    }

    public bool IsInitialized => _isInitialized;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        if (_isInitialized)
            return;
        CreateSession();
        _isInitialized = true;
    }

    public void Reset()
    {
        Teardown();
        CreateSession();
        _isInitialized = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Teardown();
    }

    // ── ICodeEvaluator ────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles and runs <paramref name="code"/>. Returns a typed outcome for all
    /// results including abort. ThreadAbortException IS caught here — ResetAbort()
    /// is called immediately and the Aborted sentinel is returned for RunGuarded to resolve.
    /// </summary>
    public EvalOutcome Evaluate(string code)
    {
        _errors!.Clear();
        var sw = Stopwatch.StartNew();

        // Temporarily replace Console.Out so we capture stdout produced by eval
        // code (e.g. Console.WriteLine) without routing it back through BepInEx's
        // log pipeline. The TeeWriter approach creates a feedback loop: BepInEx's
        // log listener writes to Console.Out, which is the TeeWriter, which writes
        // back to BepInEx's listener, ad infinitum.
        var previousOut = Console.Out;
        var capture = new System.IO.StringWriter();
        Console.SetOut(capture);
        try
        {
            CompiledMethod? compiled = _evaluator!.Compile(code);

            if (_errors.Length > 0)
            {
                return EvalOutcome.CompileError(_errors.ToString().Trim(),
                    Stdout(capture), sw.ElapsedMilliseconds);
            }

            if (compiled != null)
            {
                object? result = null;
                compiled.Invoke(ref result);
                sw.Stop();
                return result != null
                    ? EvalOutcome.Ok(result, result.GetType().FullName, Stdout(capture), sw.ElapsedMilliseconds)
                    : EvalOutcome.OkVoid(Stdout(capture), sw.ElapsedMilliseconds);
            }

            return EvalOutcome.OkVoid(Stdout(capture), sw.ElapsedMilliseconds);
        }
        catch (ThreadAbortException)
        {
            // Call ResetAbort immediately — before the finally block runs — so that
            // Console.SetOut() in the finally executes in a normal (non-abort) context.
            // Delaying ResetAbort until the caller (RunGuarded) causes Mono to run
            // Console.SetOut() under a pending abort, which stalls for seconds.
            // Return a sentinel so RunGuarded knows the eval was aborted.
            Thread.ResetAbort();
            sw.Stop();
            Console.SetOut(previousOut);
            capture.Dispose();
            return EvalOutcome.Aborted;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return EvalOutcome.RuntimeError(ex.Message, ex.StackTrace, Stdout(capture), sw.ElapsedMilliseconds);
        }
        finally
        {
            // Safe to call again after the TAE catch block already did this:
            // StringWriter.Dispose() is idempotent and Console.SetOut() is harmless
            // when called twice with the same writer. The finally ensures cleanup on
            // all non-abort exit paths (success, compile error, runtime error).
            Console.SetOut(previousOut);
            capture.Dispose();
        }
    }

    private static string? Stdout(System.IO.StringWriter w)
    {
        var s = w.ToString();
        return s.Length > 0 ? s : null;
    }

    public CompletionResult Complete(string code, int cursorPos)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var slice = cursorPos >= 0 && cursorPos < code.Length ? code.Substring(0, cursorPos) : code;
            var completions = _evaluator!.GetCompletions(slice, out _) ?? Array.Empty<string>();
            return new CompletionResult(completions, sw.ElapsedMilliseconds);
        }
        catch
        {
            return new CompletionResult(Array.Empty<string>(), sw.ElapsedMilliseconds);
        }
    }

    public void ReferenceAssembly(Assembly assembly) => TryReference(assembly);

    /// <summary>
    /// Runs a statement silently — no capture, no history, no timeout.
    /// Used to inject using directives and helper references during initialization.
    /// </summary>
    public void RunInternal(string code)
    {
        _errors!.Clear();
        try
        { _evaluator!.Compile(code); }
        catch { /* Silently tolerate — e.g. UnityEngine not present in test builds. */ }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void CreateSession()
    {
        _errors = new StringBuilder();
        var printer = new SbReportPrinter(_errors);

        var settings = new CompilerSettings
        {
            Version = LanguageVersion.Experimental,
            GenerateDebugInfo = false,
            StdLib = true,
            Target = Target.Library,
            WarningLevel = 0,
            EnhancedWarnings = false,
        };

        var context = new CompilerContext(settings, printer);
        _evaluator = new Mono.CSharp.Evaluator(context);

        // Reference every assembly already in the AppDomain.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            TryReference(asm);

        // Keep up with assemblies loaded after initialization
        // (AssetBundle loads, late plugins, etc.).
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

        // Open default namespaces.
        foreach (var ns in DefaultUsings)
            RunInternal($"using {ns};");
    }

    private void Teardown()
    {
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        _evaluator = null;
        _errors = null;
        _isInitialized = false;
    }

    private void TryReference(Assembly asm)
    {
        try
        {
            var name = asm.GetName().Name;
            if (string.IsNullOrEmpty(name))
                return;
            if (AssemblyFilter.IsFiltered(name))
                return;

            _evaluatorCallDepth++;
            try
            {
                _evaluator?.ReferenceAssembly(asm);
            }
            finally
            {
                _evaluatorCallDepth--;
            }
        }
        catch
        {
            // Collectible assemblies can throw — silently skip.
        }
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs e)
    {
        // Block re-entrant calls: AssemblyLoad fires synchronously on the same
        // thread as Compile()/ReferenceAssembly(). Calling ReferenceAssembly from
        // within itself causes the evaluator to regenerate its internal class,
        // which re-fires AssemblyLoad — infinite loop.
        if (_evaluatorCallDepth > 0)
        {
            _host.LogDebug($"skipped re-entrant load: {e.LoadedAssembly.GetName().Name}");
            return;
        }
        TryReference(e.LoadedAssembly);
    }

    // ── Compiler error sink ───────────────────────────────────────────────────

    private sealed class SbReportPrinter : ReportPrinter
    {
        private readonly StringBuilder _sb;
        public SbReportPrinter(StringBuilder sb) => _sb = sb;

        public override void Print(AbstractMessage msg, bool showFullPath)
        {
            if (msg.IsWarning)
                return; // suppress warnings; they're noise in a REPL
            _sb.AppendLine(msg.ToString());
        }
    }
}
