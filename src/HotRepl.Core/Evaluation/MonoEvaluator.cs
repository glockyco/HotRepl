using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Mono.CSharp;

namespace HotRepl.Evaluation
{
    /// <summary>
    /// Compiles and executes C# code at runtime using Mono's embedded compiler.
    /// Follows the UnityExplorer ScriptEvaluator pattern with structured results,
    /// stdout capture, and timing.
    /// </summary>
    internal sealed class MonoEvaluator : ICodeEvaluator
    {
        private readonly IReadOnlyList<Assembly> _initialReferences;
        private readonly IReadOnlyList<string> _initialUsings;

        private Evaluator _evaluator = null!; // Initialized in Init()
        private StringBuilder _compilerOutput = null!;
        private StringWriter _compilerWriter = null!;
        private StreamReportPrinter _reporter = null!;

        /// <summary>
        /// Creates a new evaluator, referencing the provided assemblies and
        /// importing the specified namespaces as default usings.
        /// </summary>
        /// <param name="references">Assemblies to make available for compilation.</param>
        /// <param name="usings">Namespaces to import as default <c>using</c> directives.</param>
        public MonoEvaluator(IReadOnlyList<Assembly> references, IReadOnlyList<string> usings)
        {
            _initialReferences = references;
            _initialUsings = usings;
            Init();
        }

        /// <inheritdoc />
        public EvalResult Evaluate(string code)
        {
            // Clear previous compiler output.
            _compilerOutput.Clear();

            // Capture Console.Out during evaluation so user code that writes
            // to stdout has its output returned alongside the result.
            var previousOut = Console.Out;
            var stdoutCapture = new StringWriter();
            Console.SetOut(stdoutCapture);

            var sw = Stopwatch.StartNew();
            try
            {
                CompiledMethod? compiled = _evaluator.Compile(code);

                if (compiled != null)
                {
                    // Expression or statement that produced a delegate — invoke it.
                    object? result = null;
                    compiled.Invoke(ref result);

                    sw.Stop();
                    string? stdout = CapturedStdout(stdoutCapture);

                    if (result != null)
                    {
                        return EvalResult.Ok(
                            result,
                            result.GetType().FullName,
                            stdout,
                            sw.ElapsedMilliseconds);
                    }

                    // Invoked successfully but no return value (e.g. void statement).
                    return EvalResult.OkVoid(stdout, sw.ElapsedMilliseconds);
                }

                // Compile() returned null — either a compilation error or a
                // successful definition (using/class/struct) that produces no delegate.
                sw.Stop();
                string? stdoutOnNull = CapturedStdout(stdoutCapture);
                string errors = _compilerOutput.ToString().Trim();

                if (errors.Length > 0)
                {
                    return EvalResult.CompilationError(errors, stdoutOnNull, sw.ElapsedMilliseconds);
                }

                // No errors, no delegate — the code was a valid definition.
                return EvalResult.OkVoid(stdoutOnNull, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                string? stdout = CapturedStdout(stdoutCapture);

                return EvalResult.RuntimeError(
                    ex.Message,
                    ex.StackTrace,
                    stdout,
                    sw.ElapsedMilliseconds);
            }
            finally
            {
                Console.SetOut(previousOut);
                stdoutCapture.Dispose();
            }
        }


        /// <inheritdoc />
        public string[] GetCompletions(string code)
        {
            try
            {
                string prefix;
                var completions = _evaluator.GetCompletions(code, out prefix);
                return completions ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }


        /// <inheritdoc />
        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            _compilerWriter?.Dispose();
        }

        /// <summary>
        /// Initializes (or re-initializes) the Mono evaluator, compiler context,
        /// assembly references, and default usings.
        /// </summary>
        private void Init()
        {
            _compilerOutput = new StringBuilder();
            _compilerWriter = new StringWriter(_compilerOutput);
            _reporter = new StreamReportPrinter(_compilerWriter);

            var settings = new CompilerSettings
            {
                Version = LanguageVersion.Experimental,
                GenerateDebugInfo = false,
                StdLib = true,
                Target = Target.Library,
                WarningLevel = 0,
                EnhancedWarnings = false,
            };

            var context = new CompilerContext(settings, _reporter);
            _evaluator = new Evaluator(context);

            // Reference all provided assemblies, skipping stdlib and mcs artifacts.
            foreach (var asm in _initialReferences)
            {
                TryReferenceAssembly(asm);
            }

            // Auto-reference assemblies loaded after initialization (e.g. from
            // AssetBundle loads or late plugin initialization).
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

            // Add default usings via Compile so they are persisted in the evaluator.
            foreach (string ns in _initialUsings)
            {
                _evaluator.Compile($"using {ns};");
            }
        }

        /// <summary>
        /// References an assembly if it isn't a stdlib duplicate or mcs autocomplete artifact.
        /// </summary>
        private void TryReferenceAssembly(Assembly assembly)
        {
            try
            {
                string name = assembly.GetName().Name;

                if (name == null)
                    return;

                // mcs autocomplete generates transient assemblies named "completions".
                if (name == "completions")
                    return;

                if (AssemblyFilter.StdLibNames.Contains(name))
                    return;

                _evaluator.ReferenceAssembly(assembly);
            }
            catch
            {
                // Dynamic assemblies, collectible assemblies, or assemblies from
                // unsupported load contexts can throw. Swallow and continue.
            }
        }

        private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
        {
            TryReferenceAssembly(args.LoadedAssembly);
        }

        /// <summary>
        /// Returns captured stdout if non-empty, null otherwise.
        /// </summary>
        private static string? CapturedStdout(StringWriter writer)
        {
            string text = writer.ToString();
            return text.Length > 0 ? text : null;
        }
    }
}
