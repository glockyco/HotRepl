using System;
using System.Collections.Generic;
using HotRepl.Evaluator;

namespace HotRepl.Evaluator.Roslyn;

public static class RoslynEvaluatorFactory
{
    private static readonly EvaluatorCapabilities[] _capabilities =
    {
        RoslynScriptEvaluator.ScriptCapabilities,
#if NET6_0_OR_GREATER
        RoslynIsolatedEvaluator.IsolatedCapabilities,
#endif
    };
    public static IReadOnlyList<EvaluatorCapabilities> Capabilities => _capabilities;

    public static ICodeEvaluator Create(string evaluatorName, IReplHost host)
    {
        if (
            string.Equals(
                evaluatorName,
                RoslynScriptEvaluator.ScriptCapabilities.Name,
                StringComparison.Ordinal
            )
        )
            return new RoslynScriptEvaluator(host);
#if NET6_0_OR_GREATER
        if (
            string.Equals(
                evaluatorName,
                RoslynIsolatedEvaluator.IsolatedCapabilities.Name,
                StringComparison.Ordinal
            )
        )
            return new RoslynIsolatedEvaluator(host);

#endif

        throw new NotSupportedException(
            $"Evaluator '{evaluatorName}' is not available in HotRepl.Evaluator.Roslyn."
        );
    }
}
