## Purpose

Define one pinned, cross-platform command environment for HotRepl development, verification, and
clean worktree initialization.

## ADDED Requirements

### Requirement: Clean shell entry

The repository SHALL provide a pinned development shell on supported Darwin and Linux systems with
the exact Bun and .NET major versions required by project manifests and all tools required by
repository hooks.

#### Scenario: Enter from a clean checkout

- **WHEN** a contributor enters the shell from a checkout containing only tracked files
- **THEN** Bun, .NET, formatters, linters, hook tooling, Python, and Git are available without
  Homebrew or user-level package installation

#### Scenario: Unsupported host

- **WHEN** a contributor evaluates the shell on an unsupported host system
- **THEN** evaluation fails with an explicit unsupported-system error

### Requirement: Explicit dependency bootstrap

The repository SHALL provide one bootstrap command that restores mutable package-manager state into
the checkout and does not modify tracked dependency declarations or lockfiles.

#### Scenario: Bootstrap a clean worktree

- **WHEN** a contributor runs bootstrap in a clean worktree
- **THEN** locked Bun packages and repository-local .NET tools are restored and tracked dependency
  files remain unchanged

#### Scenario: Bootstrap outside the repository

- **WHEN** bootstrap runs outside a HotRepl checkout
- **THEN** it fails before writing files and states that it must run from the repository root

### Requirement: One verification environment

Local hooks and continuous integration SHALL run repository checks through the pinned environment
and SHALL not contain a second independent tool installation path.

#### Scenario: Local pre-push verification

- **WHEN** a contributor runs the canonical pre-push gate
- **THEN** the gate resolves every tool from the pinned environment and exercises the same project
  commands as continuous integration

#### Scenario: Continuous integration

- **WHEN** a pull request runs continuous integration
- **THEN** jobs enter the pinned environment from the committed lock and do not download separately
  versioned developer tools
