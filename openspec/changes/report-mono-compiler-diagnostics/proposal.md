## Why

The BepInEx evaluator discards Mono compiler diagnostics and returns text such as
`(1,1): InteractiveHost`. Users cannot identify or correct invalid C# submissions from this output.

## What Changes

- Define the diagnostic contract for failed C# submissions.
- Report each Mono compiler error with its source location, severity, `CS` code, and message text.
- Preserve Mono's diagnostic ordering and related-symbol information.
- Continue to suppress compiler warnings without converting a successful submission into a compile
  error.
- Add behavioral coverage for the Mono report-printer adapter and verify the concrete evaluator in a
  live BepInEx Unity Mono host.
- Correct the protocol documentation to show both evaluators that the BepInEx host exposes.
- Keep the evaluator default, submission semantics, and protocol envelope unchanged.

## Capabilities

### New Capabilities

- `csharp-evaluation`: Defines C# submission results and actionable compile-error diagnostics.

### Modified Capabilities

None.

## Impact

- `src/HotRepl.Evaluator.MonoCSharp/MonoCSharpEvaluator.cs`
- `tests/HotRepl.Tests/`
- `docs/control-plane-protocol.md`
- The `eval_error.error.message` text for failed Mono.CSharp submissions

The change adds no dependency and does not change the protocol schema. `Roslyn.Script` remains
available as an explicit evaluator choice. A future default change requires runtime memory, latency,
cancellation, and completion evidence from supported Unity hosts.
