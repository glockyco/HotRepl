## Purpose

Define how game repositories build HotRepl loader artifacts from a pinned source revision and
caller-owned game assemblies without sibling checkouts.

## ADDED Requirements

### Requirement: Revision-pinned build entry point

HotRepl SHALL expose one non-interactive loader build command that can be invoked from a pinned
repository revision and accepts an explicit loader target, game-assembly input, and writable output
directory.

#### Scenario: Build BepInEx artifact

- **WHEN** a caller selects BepInEx and supplies the required Unity assembly directory
- **THEN** the command builds the BepInEx host from its pinned HotRepl source and writes the
  complete deployable sidecar set only to the requested output directory

#### Scenario: Build MelonLoader artifact

- **WHEN** a caller selects MelonLoader and supplies explicit Unity, MelonLoader, and IL2CPP
  assembly directories
- **THEN** the command builds the MelonLoader host from its pinned HotRepl source and writes the
  complete deployable sidecar set only to the requested output directory

### Requirement: Fail-closed external inputs

The loader build command SHALL validate every required caller-supplied path before restoring or
compiling and SHALL reject missing, ambiguous, or incompatible loader inputs.

#### Scenario: Required assembly is missing

- **WHEN** a required game or loader assembly is absent
- **THEN** the command exits nonzero, identifies the missing file, and leaves no successful output
  manifest

#### Scenario: Output aliases an input

- **WHEN** the requested output directory resolves inside the immutable source or caller-supplied
  assembly directories
- **THEN** the command exits nonzero before modifying either input

### Requirement: Build provenance

Each successful loader build SHALL emit a machine-readable manifest beside the artifacts that
records the HotRepl revision, loader target, configuration, tool versions, input assembly SHA-256
hashes, and output SHA-256 hashes.

#### Scenario: Successful build manifest

- **WHEN** a loader build completes successfully
- **THEN** the manifest contains enough stable identifiers and hashes for a consumer to prove which
  source and external inputs produced every deployable output

### Requirement: Consumer-owned deployment

The build command SHALL stop at an owned output directory and SHALL NOT discover or modify a game
installation, CrossOver bottle, loader installation, or deployment destination.

#### Scenario: Build for a CrossOver game

- **WHEN** a macOS caller supplies assemblies copied or referenced from a CrossOver game
- **THEN** the command reads those inputs, writes the requested output, and does not modify the
  bottle
