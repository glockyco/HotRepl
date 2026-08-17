# C# Evaluation Specification

## Purpose

Defines how HotRepl reports C# submission results so users and clients can distinguish successful
execution from actionable compiler failures.

## Requirements

### Requirement: Compile failures contain actionable diagnostics

When a C# submission does not compile, HotRepl SHALL return a `validation_failed` error with code
`compileError`. Its message SHALL contain each compiler error's source location, severity, compiler
code, and message text when the compiler supplies those fields.

#### Scenario: Mono rejects an invalid return statement

- **WHEN** a user submits `return 5;` to the Mono.CSharp evaluator
- **THEN** evaluation fails with error kind `validation_failed` and code `compileError`
- **AND** the message contains `(1,2): error CS0127:`
- **AND** the message contains
  `A return keyword must not be followed by any expression when method returns void`

#### Scenario: Compiler reports multiple errors

- **WHEN** a C# submission produces multiple compiler errors
- **THEN** the message contains every error in compiler order
- **AND** each error starts on a separate line

#### Scenario: Compiler supplies related-symbol information

- **WHEN** a compiler error includes related-symbol information
- **THEN** the error message contains that information after the primary diagnostic

### Requirement: Warnings do not become compile failures

A compiler warning SHALL NOT cause an otherwise valid C# submission to return a compile error.

#### Scenario: Valid submission emits a warning

- **WHEN** a valid C# submission produces a compiler warning and no compiler error
- **THEN** evaluation returns the submission's successful result

### Requirement: Evaluators preserve their declared semantics

The diagnostic format SHALL NOT change an evaluator's language version, persistent-state behavior,
completion support, cancellation mode, or submission result.

#### Scenario: Mono trailing expression succeeds

- **WHEN** a user submits `var value = 1; value + 1` to the Mono.CSharp evaluator
- **THEN** evaluation returns the integer value `2`
- **AND** the evaluator retains its declared capabilities
