#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using HotRepl.Evaluator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HotRepl.Evaluator.Roslyn;

public sealed class RoslynIsolatedEvaluator : ICodeEvaluator
{
    public static readonly EvaluatorCapabilities IsolatedCapabilities = new()
    {
        Name = "Roslyn.Isolated",
        LanguageVersion = "latest",
        SupportsPersistentState = false,
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
    private readonly List<Assembly> _referencedAssemblies = new();
    private readonly HashSet<string> _imports = new(StringComparer.Ordinal);
    private bool _isInitialized;

    public RoslynIsolatedEvaluator(IReplHost host) => _host = host;

    public bool IsInitialized => _isInitialized;
    public EvaluatorCapabilities Capabilities => IsolatedCapabilities;
    public bool PendingHotReload => false;
    public string? PendingHotReloadAssembly => null;

    public void Initialize() => _isInitialized = true;

    public EvalOutcome Evaluate(string code, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var previousOut = Console.Out;
        using var capture = new StringWriter();
        Console.SetOut(capture);

        try
        {
            var result = CompileAndRun(code, cancellationToken);
            sw.Stop();
            return result != null
                ? EvalOutcome.Ok(
                    result,
                    result.GetType().FullName,
                    Stdout(capture),
                    sw.ElapsedMilliseconds
                )
                : EvalOutcome.OkVoid(Stdout(capture), sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return EvalOutcome.Aborted;
        }
        catch (CompilationFailedException ex)
        {
            sw.Stop();
            return EvalOutcome.CompileError(ex.Message, Stdout(capture), sw.ElapsedMilliseconds);
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
        _referencedAssemblies.Clear();
        _imports.Clear();
        _isInitialized = true;
    }

    public void ReferenceAssembly(Assembly assembly)
    {
        if (!assembly.IsDynamic && !string.IsNullOrEmpty(SafeLocation(assembly)))
            _referencedAssemblies.Add(assembly);
    }

    public void RunInternal(string code)
    {
        var trimmed = code.Trim();
        if (trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(';'))
            _imports.Add(
                trimmed.Substring("using ".Length, trimmed.Length - "using ".Length - 1).Trim()
            );
    }

    public void Dispose() { }

    private object? CompileAndRun(string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var imports = DefaultImports
            .Concat(_host.AdditionalUsings)
            .Concat(_imports)
            .Distinct(StringComparer.Ordinal)
            .Select(ns => $"using {ns};");
        var source =
            string.Join("\n", imports)
            + "\npublic static class __HotReplSnippet { public static object? Run() { "
            + code
            + "\nreturn null; } }";
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = AppDomain
            .CurrentDomain.GetAssemblies()
            .Concat(_host.AdditionalAssemblies)
            .Concat(_referencedAssemblies)
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(SafeLocation(a)))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Distinct()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "HotRepl.Isolated.Snippet",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe, cancellationToken: cancellationToken);
        if (!emit.Success)
            throw new CompilationFailedException(
                string.Join(Environment.NewLine, emit.Diagnostics)
            );

        pe.Position = 0;
        var alc = new AssemblyLoadContext("HotRepl.Isolated", isCollectible: true);
        WeakReference? weak = null;
        try
        {
            var asm = alc.LoadFromStream(pe);
            weak = new WeakReference(asm, trackResurrection: false);
            var type = asm.GetType("__HotReplSnippet", throwOnError: true)!;
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return method.Invoke(null, Array.Empty<object>());
        }
        finally
        {
            alc.Unload();
            _ = weak;
        }
    }

    private static string? SafeLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch
        {
            return null;
        }
    }

    private static string? Stdout(StringWriter writer)
    {
        var value = writer.ToString();
        return value.Length > 0 ? value : null;
    }

    private sealed class CompilationFailedException : Exception
    {
        public CompilationFailedException(string message)
            : base(message) { }
    }
}
#endif
