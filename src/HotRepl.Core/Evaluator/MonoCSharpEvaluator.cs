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
/// Evaluate() may raise ThreadAbortException when the engine's watchdog fires;
/// the caller (ReplEngine) is responsible for catching it and calling Thread.ResetAbort().
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
    /// non-abort results. ThreadAbortException is NOT caught here — it propagates
    /// to the engine's ExecuteEval / GuardedEvaluate, which owns ResetAbort().
    /// </summary>
    public EvalOutcome Evaluate(string code)
    {
        _errors!.Clear();
        var sw = Stopwatch.StartNew();

        StdoutCapture.BeginCapture();
        try
        {
            CompiledMethod? compiled = _evaluator!.Compile(code);

            if (_errors.Length > 0)
            {
                // Compile error: EndCapture discards any partial stdout.
                var stdout1 = StdoutCapture.EndCapture();
                return EvalOutcome.CompileError(_errors.ToString().Trim(), stdout1, sw.ElapsedMilliseconds);
            }

            if (compiled != null)
            {
                object? result = null;
                compiled.Invoke(ref result);
                sw.Stop();

                var stdout2 = StdoutCapture.EndCapture();
                return result != null
                    ? EvalOutcome.Ok(result, result.GetType().FullName, stdout2, sw.ElapsedMilliseconds)
                    : EvalOutcome.OkVoid(stdout2, sw.ElapsedMilliseconds);
            }

            // Valid definition (class, using, etc.) — no return value.
            var stdout3 = StdoutCapture.EndCapture();
            return EvalOutcome.OkVoid(stdout3, sw.ElapsedMilliseconds);
        }
        catch (ThreadAbortException)
        {
            // Clean up capture, then let the engine handle the abort.
            StdoutCapture.EndCapture();
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var stdout4 = StdoutCapture.EndCapture();
            return EvalOutcome.RuntimeError(ex.Message, ex.StackTrace, stdout4, sw.ElapsedMilliseconds);
        }
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
            _evaluator?.ReferenceAssembly(asm);
        }
        catch
        {
            // Dynamic / collectible assemblies can throw — silently skip.
        }
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs e)
        => TryReference(e.LoadedAssembly);

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
