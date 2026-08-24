# agent-guidance Specification

## Purpose

Defines how HotRepl exposes accurate repository guidance without directing agents to retired
workflows, nonexistent commands, or duplicated global policy.

## Requirements

### Requirement: Guidance describes the supported checkout workflow

Repository guidance SHALL describe the primary checkout as the development location. It SHALL NOT
recommend creating or bootstrapping additional Git worktrees.

#### Scenario: An agent prepares the repository

- **WHEN** an agent reads the repository setup guidance
- **THEN** it is directed to restore dependencies in the primary checkout
- **AND** no discovered skill or documented command recommends another worktree

#### Scenario: An obsolete setup route is searched

- **WHEN** repository guidance and public flake applications are inspected
- **THEN** no `bootstrap-worktree` skill, script, application, or layout convention is present

### Requirement: Operational examples name registered commands

Every first-party typed-command example in repository guidance SHALL name a command registered by a
first-party catalog. An automated check SHALL reject an unknown command name.

#### Scenario: A skill demonstrates a typed command

- **WHEN** an operational skill contains a first-party command example
- **THEN** the example names a command in the registered first-party catalog
- **AND** the documented arguments satisfy that command's descriptor

#### Scenario: An example names an unknown command

- **WHEN** a guidance change introduces a command name absent from the first-party catalog
- **THEN** the repository guidance check fails and names the command and file

### Requirement: Global policy is not copied into project skills

Repository guidance SHALL rely on discovered global policy for generic commit mechanics. It MAY
retain repository-specific constraints that an enforced configuration owns.

#### Scenario: An agent prepares a commit

- **WHEN** an agent reads HotRepl guidance before committing
- **THEN** generic commit mechanics route to the global `commit-policy` skill
- **AND** repository guidance does not duplicate its format, staging, checkpoint, or push rules

#### Scenario: The repository enforces a local commit value

- **WHEN** `commitlint.config.js` enforces a repository-specific type or scope
- **THEN** concise local guidance MAY name that value

### Requirement: Setup failures have an accurate owner

The pinned development environment SHALL own required development tools. Missing optional Unity
assemblies SHALL be reported as an explicit host-build prerequisite, not as evidence that another
checkout is required.

#### Scenario: Required development tools are absent

- **WHEN** the repository setup or doctor command runs without a required development tool
- **THEN** it reports the missing tool through the pinned environment path
- **AND** it does not recommend installing an unpinned user toolchain

#### Scenario: Optional host assemblies are absent

- **WHEN** a contributor requests a BepInEx host build without its local Unity assemblies
- **THEN** guidance identifies the missing host-build prerequisite
- **AND** Core-only work remains available without those assemblies
