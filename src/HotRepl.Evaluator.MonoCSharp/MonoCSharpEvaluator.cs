using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using HotRepl.Evaluator;
using Mono.CSharp;

namespace HotRepl.Evaluator.MonoCSharp;

/// <summary>
/// Wraps Mono.CSharp.Evaluator to compile and execute C# 7.x code at runtime.
///
/// Threading: all public methods MUST be called from the Unity main thread.
/// Evaluate() catches ThreadAbortException internally when the engine's watchdog fires,
/// calls ResetAbort(), and returns an Aborted sentinel for RunGuarded to resolve.
/// </summary>
public sealed class MonoCSharpEvaluator : ICodeEvaluator, IDisposable
{
    // Assemblies we never want to reference: mcs autocomplete artifacts and
    // stdlib duplicates that the Mono evaluator already loads implicitly.

    // Default namespaces opened in every evaluator session.
    // Exposed internally so ReplEngine can include them in the handshake defaultUsings[].
    public static readonly EvaluatorCapabilities MonoCapabilities = new()
    {
        Name = "Mono.CSharp",
        LanguageVersion = "7.x",
        SupportsPersistentState = true,
        SupportsCompletion = true,
        TimeoutMode = TimeoutMode.HardAbort,
    };

    public static readonly string[] DefaultUsings =
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
    private StringWriter? _diagnostics;
    private bool _isInitialized;
    private bool _disposed;

    // Tracks how many levels deep we are inside a Mono evaluator call.
    // AssemblyLoad events fire synchronously on the same thread. If we're
    // already inside Compile() or ReferenceAssembly(), a re-entrant call to
    // ReferenceAssembly() in OnAssemblyLoad would cause the evaluator to
    // regenerate its internal class, re-fire AssemblyLoad, and loop forever.
    // Blocking re-entry when depth > 0 breaks the cycle.
    private int _evaluatorCallDepth;

    // Set by OnAssemblyLoad when a ScriptEngine hot-reload assembly is detected.
    // ReplEngine checks this in Tick() and performs the full reset (including
    // helper re-injection and client notification).
    private string? _pendingHotReloadAssembly;

    public MonoCSharpEvaluator(IReplHost host)
    {
        _host = host;
    }

    public bool IsInitialized => _isInitialized;
    public EvaluatorCapabilities Capabilities => MonoCapabilities;
    public bool PendingHotReload => _pendingHotReloadAssembly != null;
    public string? PendingHotReloadAssembly => _pendingHotReloadAssembly;

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
        _pendingHotReloadAssembly = null;
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
    public EvalOutcome Evaluate(string code, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _diagnostics!.GetStringBuilder().Clear();
        var sw = Stopwatch.StartNew();
        try
        {
            return CompileAndRun(code, sw);
        }
        catch (ThreadAbortException)
        {
            // The engine's watchdog aborted the main thread. Reset the abort so the
            // engine can resolve the sentinel into Timeout or Cancelled.
            Thread.ResetAbort();
            return EvalOutcome.Aborted;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return EvalOutcome.RuntimeError(ex.Message, ex.StackTrace, sw.ElapsedMilliseconds);
        }
    }

    private EvalOutcome CompileAndRun(string code, Stopwatch sw)
    {
        CompiledMethod? compiled;
        try
        {
            compiled = _evaluator!.Compile(code);
        }
        catch (Exception ex) when (ex is not ThreadAbortException)
        {
            // Ordinary compile errors reach the report printer. An exception from Compile means
            // the compiler itself failed, for example while emitting an anonymous method.
            sw.Stop();
            return EvalOutcome.CompileError(DescribeCompilerFailure(ex), sw.ElapsedMilliseconds);
        }

        if (_diagnostics!.GetStringBuilder().Length > 0)
            return EvalOutcome.CompileError(_diagnostics.ToString().Trim(), sw.ElapsedMilliseconds);

        if (compiled != null)
        {
            object? result = null;
            compiled.Invoke(ref result);
            sw.Stop();
            return result != null
                ? EvalOutcome.Ok(result, result.GetType().FullName, sw.ElapsedMilliseconds)
                : EvalOutcome.OkVoid(sw.ElapsedMilliseconds);
        }

        return EvalOutcome.OkVoid(sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Mono.CSharp wraps code-generation failures in <see cref="InternalErrorException"/>, whose
    /// message names only the source location and the member being emitted. The cause is in the
    /// inner exceptions, so the description lists the whole chain.
    /// </summary>
    private static string DescribeCompilerFailure(Exception failure)
    {
        var text = new System.Text.StringBuilder("Internal compiler error");
        for (Exception? e = failure; e != null; e = e.InnerException)
        {
            text.Append(Environment.NewLine);
            text.Append(
                e is InternalErrorException ? e.Message : e.GetType().FullName + ": " + e.Message
            );
        }
        return text.ToString();
    }

    public CompletionResult Complete(string code, int cursorPos)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var slice =
                cursorPos >= 0 && cursorPos < code.Length ? code.Substring(0, cursorPos) : code;
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
        _diagnostics!.GetStringBuilder().Clear();
        try
        {
            _evaluator!.Compile(code);
        }
        catch
        { /* Silently tolerate — e.g. UnityEngine not present in test builds. */
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void CreateSession()
    {
        _diagnostics = new StringWriter(CultureInfo.InvariantCulture);
        var printer = new ErrorOnlyStreamReportPrinter(_diagnostics);

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
        // Superseded ScriptEngine assemblies are filtered out so types
        // resolve to the newest version after hot reload.
        var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in allAssemblies)
            TryReference(asm, allAssemblies);

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
        _diagnostics?.Dispose();
        _diagnostics = null;
        _isInitialized = false;
    }

    private void TryReference(Assembly asm, Assembly[]? allAssemblies = null)
    {
        try
        {
            var name = asm.GetName().Name;
            if (string.IsNullOrEmpty(name))
                return;
            if (AssemblyFilter.IsFiltered(name))
                return;
            if (allAssemblies != null && AssemblyFilter.IsSuperseded(asm, allAssemblies))
            {
                _host.LogDebug($"skipped superseded assembly: {name}");
                return;
            }

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

        var name = e.LoadedAssembly.GetName().Name ?? "";

        // Detect ScriptEngine hot reload: a new assembly with a timestamp suffix
        // whose base name matches an already-referenced assembly. Rebuild the
        // entire evaluator session so types resolve to the newest version.
        if (AssemblyFilter.TryParseScriptEngineName(name, out var baseName, out _))
        {
            _host.LogInfo($"[HotRepl] Hot-reload detected ({name}). Rebuilding evaluator session.");
            _pendingHotReloadAssembly = baseName;
            // Don't reference the new assembly here — Reset() will pick it up
            // during CreateSession and filter out superseded versions.
            return;
        }

        TryReference(e.LoadedAssembly);
    }
}
