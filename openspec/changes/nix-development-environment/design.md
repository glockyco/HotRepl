## Context

See `proposal.md` for motivation. HotRepl spans Bun workspaces, .NET projects, repository-local
tools, shell scripts, Lefthook, and GitHub Actions. Host projects require proprietary or
game-distributed assemblies that cannot enter this repository or a public binary cache. The
repository source is immutable when invoked through a flake, while .NET restore and build require
writable intermediate directories.

## Goals / Non-Goals

**Goals:**

- Make the flake lock the single owner of developer and CI tool versions.
- Keep automatic shell entry cheap and free of avoidable source compiler chains on Darwin.
- Give consumers a revision-pinned command with explicit external inputs and verifiable outputs.
- Preserve existing C# and TypeScript build behavior.

**Non-Goals:**

- Manage CrossOver bottles, games, loaders, saves, or deployment.
- Put proprietary Unity, BepInEx, MelonLoader, or IL2CPP assemblies in the Nix store or repository.
- Convert every .NET project into a sandboxed Nix derivation.
- Migrate game repositories in this change.

## Decisions

### Use one flake package set on four host systems

The flake exposes `devShells`, `apps`, and checks for `aarch64-darwin`, `x86_64-darwin`,
`aarch64-linux`, and `x86_64-linux`. Bun comes from the pinned package set and must match
`package.json`. Darwin .NET uses Nixpkgs' fixed-output binary SDK variants to avoid Swift and
source-built VMR closures during shell refresh.

Alternative: retain Homebrew and setup actions. Rejected because each surface owns separate versions
and clean worktrees are not reproducible.

### Restore mutable state explicitly

The development shell only supplies tools and environment variables. `nix run .#bootstrap` restores
locked Bun dependencies and local .NET tools into the writable checkout. Shell entry never mutates
the worktree.

Alternative: restore from `shellHook`. Rejected because directory entry would perform network and
filesystem mutations.

### Build downstream loaders in an isolated writable workspace

`nix run <pinned-HotRepl>#build-loader -- ...` copies the immutable repository source to a temporary
writable directory, validates and links explicit external assemblies, restores locked dependencies,
builds one loader, and copies only deployable outputs to a fresh caller-owned directory. The command
emits provenance after artifact hashing and atomically promotes the completed staging directory.

Alternative: export a `buildDotnetModule` function. Rejected for the first contract because
proprietary game assemblies are mutable host inputs and cannot safely be imported into a public Nix
derivation or cache. A consumer flake can still wrap the application.

Alternative: build the caller's sibling checkout. Rejected because it loses revision identity and
couples consumers to filesystem layout.

### Keep deployment in consumer repositories

The build command never parses Steam manifests and never selects a bottle or game path. Consumers
already own those policies and can deploy the manifest-backed output through their canonical CLI.

### Route hooks and CI through Nix

Lefthook commands invoke a small repository wrapper that runs inside the current Nix shell or
re-enters it with `nix develop --command`. CI installs Nix once, then invokes the same pre-push
gate. The wrapper has no Homebrew or user-path fallback.

## Risks / Trade-offs

- [First invocation fetches the pinned Nix closure] → Keep shell closure binary-backed on Darwin and
  expose a dry build-plan diagnostic.
- [External game assemblies change without a repository diff] → Hash every consumed assembly in the
  output manifest.
- [A failed build leaves misleading artifacts] → Build into sibling staging, omit the manifest on
  failure, and atomically replace only a complete output directory.
- [Git metadata is absent in flake source] → Derive revision identity from flake-provided source
  metadata, with an explicit dirty-local marker for checkout invocation.
- [CI Nix setup adds startup time] → Cache the Nix store and remove duplicated per-job tool
  downloads.

## Migration Plan

1. Add and validate OpenSpec contracts before implementation.
2. Add the flake, bootstrap, environment wrapper, loader builder, and provenance generation.
3. Migrate repository hooks and CI to the wrapper, then delete ambient tool discovery.
4. Verify a clean Core-only checkout and one real BepInEx build with local Unity assemblies.
5. Publish and merge the HotRepl change after required checks pass.
6. Migrate Ardenfall, Ancient Kingdoms, and Erenshor in separate repository-owned changes.

Rollback reverts the implementation commits. Existing package manifests and runtime projects remain
unchanged, so the previous ambient commands continue to work after the revert.
