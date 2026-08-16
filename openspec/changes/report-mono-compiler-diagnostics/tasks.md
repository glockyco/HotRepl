## 1. Behavioral Coverage

- [x] 1.1 Add Mono report-printer adapter tests for canonical single-error, multiple-error, and
      related-symbol diagnostics.
- [x] 1.2 Add report-printer and capability tests that preserve warning suppression and the declared
      Mono evaluator capabilities.

## 2. Diagnostic Reporting

- [x] 2.1 Replace the custom diagnostic formatter with a warning-filtering `StreamReportPrinter`
      implementation and an explicitly managed `StringWriter` buffer.
- [x] 2.2 Verify that reset and disposal release the diagnostic buffer with the Mono compiler
      session.

## 3. Contract Documentation

- [x] 3.1 Correct the BepInEx handshake example to list `Mono.CSharp` and `Roslyn.Script` while
      retaining `Mono.CSharp` as the default.

## 4. Verification

- [x] 4.1 Run the HotRepl C# evaluator test suite.
- [x] 4.2 Run `nix run .#check`.
- [x] 4.3 Deploy HotRepl to Ardenfall, start the game, and verify canonical `CS0127` output, warning
      suppression, and trailing-expression results through the CLI.
- [x] 4.4 Stop Ardenfall after the live smoke test.
