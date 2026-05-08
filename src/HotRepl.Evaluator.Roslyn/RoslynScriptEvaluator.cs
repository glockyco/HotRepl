using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HotRepl.Evaluator;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace HotRepl.Evaluator.Roslyn;

public sealed class RoslynScriptEvaluator : ICodeEvaluator
{
    public static readonly EvaluatorCapabilities ScriptCapabilities = new()
    {
        Name = "Roslyn.Script",
        LanguageVersion = "latest",
        SupportsPersistentState = true,
        SupportsCompletion = false,
        TimeoutMode = TimeoutMode.Cooperative,
    };

    private static readonly string[] DefaultImports =
    {
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Linq",
        "System.Reflection",
    };

    private readonly IReplHost _host;
    private ScriptOptions _options = ScriptOptions.Default;
    private ScriptState<object>? _state;
    private bool _isInitialized;
    private bool _disposed;

    public RoslynScriptEvaluator(IReplHost host) => _host = host;

    public bool IsInitialized => _isInitialized;
    public EvaluatorCapabilities Capabilities => ScriptCapabilities;
    public bool PendingHotReload => false;
    public string? PendingHotReloadAssembly => null;

    public void Initialize()
    {
        if (_isInitialized)
            return;

        _options = BuildOptions();
        _isInitialized = true;
    }

    public EvalOutcome Evaluate(string code, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        var sw = Stopwatch.StartNew();
        var previousOut = Console.Out;
        using var capture = new StringWriter();
        Console.SetOut(capture);

        try
        {
            _state =
                _state == null
                    ? CSharpScript
                        .RunAsync(code, _options, cancellationToken: cancellationToken)
                        .GetAwaiter()
                        .GetResult()
                    : _state
                        .ContinueWithAsync(code, _options, cancellationToken)
                        .GetAwaiter()
                        .GetResult();

            sw.Stop();
            var value = _state.ReturnValue;
            return value != null
                ? EvalOutcome.Ok(
                    value,
                    value.GetType().FullName,
                    Stdout(capture),
                    sw.ElapsedMilliseconds
                )
                : EvalOutcome.OkVoid(Stdout(capture), sw.ElapsedMilliseconds);
        }
        catch (CompilationErrorException ex)
        {
            sw.Stop();
            return EvalOutcome.CompileError(
                string.Join(Environment.NewLine, ex.Diagnostics),
                Stdout(capture),
                sw.ElapsedMilliseconds
            );
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return EvalOutcome.Aborted;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return EvalOutcome.RuntimeError(
                ex.Message,
                ex.StackTrace,
                Stdout(capture),
                sw.ElapsedMilliseconds
            );
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    }

    public CompletionResult Complete(string code, int cursorPos) => new(Array.Empty<string>(), 0);

    public void Reset()
    {
        _state = null;
        _options = BuildOptions();
        _isInitialized = true;
    }

    public void ReferenceAssembly(Assembly assembly)
    {
        if (CanReference(assembly))
            _options = _options.AddReferences(assembly);
    }

    public void RunInternal(string code)
    {
        EnsureInitialized();
        try
        {
            _state =
                _state == null
                    ? CSharpScript.RunAsync(code, _options).GetAwaiter().GetResult()
                    : _state.ContinueWithAsync(code, _options).GetAwaiter().GetResult();
        }
        catch
        {
            // Initialization imports are best-effort because Unity assemblies are
            // not present in unit-test runs.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _state = null;
    }

    private ScriptOptions BuildOptions()
    {
        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Concat(_host.AdditionalAssemblies)
            .Where(CanReference)
            .Distinct()
            .ToArray();

        return ScriptOptions
            .Default.WithReferences(assemblies)
            .WithImports(DefaultImports.Concat(_host.AdditionalUsings));
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
            Initialize();
    }

    private static bool CanReference(Assembly assembly)
    {
        if (assembly.IsDynamic)
            return false;
        try
        {
            return !string.IsNullOrEmpty(assembly.Location);
        }
        catch
        {
            return false;
        }
    }

    private static string? Stdout(StringWriter writer)
    {
        var value = writer.ToString();
        return value.Length > 0 ? value : null;
    }
}
