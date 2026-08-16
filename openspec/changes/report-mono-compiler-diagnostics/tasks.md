## 1. Behavioral Coverage

- [ ] 1.1 Add Mono report-printer adapter tests for canonical single-error, multiple-error, and
      related-symbol diagnostics.
- [ ] 1.2 Add report-printer and capability tests that preserve warning suppression and the declared
      Mono evaluator capabilities.

## 2. Diagnostic Reporting

- [ ] 2.1 Replace the custom diagnostic formatter with a warning-filtering `StreamReportPrinter`
      implementation and an explicitly managed `StringWriter` buffer.
- [ ] 2.2 Verify that reset and disposal release the diagnostic buffer with the Mono compiler
      session.

## 3. Contract Documentation

- [ ] 3.1 Correct the BepInEx handshake example to list `Mono.CSharp` and `Roslyn.Script` while
      retaining `Mono.CSharp` as the default.

## 4. Verification

- [ ] 4.1 Run the HotRepl C# evaluator test suite.
- [ ] 4.2 Run `nix run .#check`.
- [ ] 4.3 Deploy HotRepl to Ardenfall, start the game, and verify canonical `CS0127` output, warning
      suppression, and trailing-expression results through the CLI.
- [ ] 4.4 Stop Ardenfall after the live smoke test.
