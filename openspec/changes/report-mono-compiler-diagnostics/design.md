## Context

See `proposal.md` for the defect. `MonoCSharpEvaluator` sends diagnostics to a custom
`ReportPrinter`. The printer suppresses warnings and calls `AbstractMessage.ToString()` for errors.
The live BepInEx host therefore returned `(1,1): InteractiveHost` for invalid submissions.

Mono already implements its diagnostic layout in `StreamReportPrinter`. That layout includes the
source location, message type, zero-padded compiler code, text, and related symbols. UnityExplorer
and RuntimeUnityEditor also use `StreamReportPrinter` with `Mono.CSharp.Evaluator`.

`EvalOutcome.CompileError` and the v2 protocol carry one message string. The CLI writes that string
to standard error. No later layer removes diagnostic fields.

The protocol document's BepInEx handshake example lists only `Mono.CSharp`. The host currently
exposes `Mono.CSharp` and `Roslyn.Script`, while it still selects `Mono.CSharp` by default. The
change will correct this observed contradiction.

Primary references:

- Mono evaluator: <https://github.com/mono/mono/blob/main/mcs/mcs/eval.cs>
- Mono report printer: <https://github.com/mono/mono/blob/main/mcs/mcs/report.cs>
- UnityExplorer evaluator:
  <https://github.com/sinai-dev/UnityExplorer/blob/master/src/CSConsole/ScriptEvaluator.cs>
- RuntimeUnityEditor evaluator:
  <https://github.com/ManlyMarco/RuntimeUnityEditor/blob/master/RuntimeUnityEditor.Core/Windows/REPL/MCS/ScriptEvaluator.cs>

## Goals / Non-Goals

**Goals:**

- Use Mono's canonical error layout.
- Preserve warning suppression and diagnostic order.
- Keep the diagnostic buffer lifecycle explicit.
- Verify the report-printer adapter under xUnit and the concrete evaluator in a live BepInEx Unity
  Mono host.

**Non-Goals:**

- Do not change the BepInEx default evaluator.
- Do not change Mono.CSharp submission parsing or C# 7.x support.
- Do not wrap submissions in generated lambdas.
- Do not change the protocol schema or add a second diagnostic representation.
- Do not update `mcs.dll`.
- Do not address the documented `varName * expr` parser ambiguity.

## Decisions

### Delegate formatting to Mono

Replace the custom `ReportPrinter` formatter with an error-only subclass of `StreamReportPrinter`.
The subclass will ignore warning messages and delegate error messages to
`StreamReportPrinter.Print`.

This design keeps Mono as the single producer of its diagnostic format. It also preserves
related-symbol lines and `showFullPath` behavior. A local formatter would copy compiler behavior and
could diverge after an `mcs.dll` update.

The pinned `ReportPrinter` defaults `HasRelatedSymbolSupport` to `false`. The adapter will override
that property so Mono collects related symbols before `StreamReportPrinter` formats them.

The evaluator will use a `StringWriter` as the diagnostic buffer. Each evaluation will clear its
underlying `StringBuilder`. Session reset and disposal will release the writer with the compiler
session.

### Keep one wire message

Keep the current `EvalOutcome.CompileError` and `eval_error.error.message` contract. The compiler
printer will produce the complete message before `EvalOutcome` receives it.

A structured diagnostics array would affect the protocol, SDKs, CLI, and compatibility policy. It
would also duplicate the required human-readable message. No current consumer requires compiler
spans as data.

### Test at the supported runtime boundaries

The pinned `mcs.dll` calls the legacy Mono `AppDomain.DefineDynamicAssembly` API. The repository's
xUnit project targets .NET 10, where that API does not exist. A concrete `MonoCSharpEvaluator` test
in that process fails before it compiles a submission.

Expose the report-printer adapter to `HotRepl.Tests` as an internal type. Unit tests will pass real
`AbstractMessage` subclasses through the adapter. They will verify the canonical location, severity,
code, text, multiple-error order, related-symbol lines, and warning suppression.

The live BepInEx Unity Mono smoke test will cover the complete path from
`MonoCSharpEvaluator.Evaluate` through the protocol and CLI. It will verify `return 5;`, a
warning-only submission, and the existing trailing-expression result. This split tests production
components at runtimes that can execute them. It does not mock or replace the compiler.

### Retain evaluator selection policy

Keep `Mono.CSharp` as the BepInEx default. Keep `Roslyn.Script` as an explicit option.

Roslyn scripting supports newer C# and persistent `ScriptState`. It also accepts script-level
`return` statements. However, HotRepl currently declares no Roslyn completion support and only
cooperative cancellation. Current evidence has no trustworthy Unity Mono measurement for cold
latency or retained memory.

A future default change requires a separate live-host study and capability-parity decision. The
study must run in a supported BepInEx Unity Mono game. It must record loaded Roslyn dependency
identities and locations, cold initialization time, per-submission time, managed memory, process
working set, and loaded-assembly growth. It must verify persistent declarations, trailing
expressions, script-level returns, statement blocks, compile diagnostics, completion, cancellation,
reset, and 100 unique submissions. The study must close the game after measurement.

## Risks / Trade-offs

- [Diagnostic wording is tied to the pinned `mcs.dll`] → Assert the fields and stable message for
  the pinned compiler. Run a live BepInEx smoke test after deployment.
- [Using `StreamReportPrinter` could capture warnings] → Filter warnings before delegation and
  verify a warning-only submission succeeds.
- [A compiler can emit several diagnostics] → Keep one writer for the evaluation and preserve Mono's
  line order.

## Migration Plan

1. Add report-printer adapter tests that fail against the current conversion.
2. Replace the diagnostic printer and buffer.
3. Run the C# test suite and repository gate.
4. Restart one BepInEx Unity host and verify compile errors, warnings, and trailing expressions
   through the CLI.
5. Close the Unity host after the smoke test.

Rollback restores the previous printer. The protocol and persisted data require no migration.
