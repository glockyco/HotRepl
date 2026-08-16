## 1. Pinned Development Environment

- [ ] 1.1 Add the four-system flake and lock with binary-backed Darwin .NET SDK selection
- [ ] 1.2 Expose clean development-shell, bootstrap, and bounded diagnostic commands
- [ ] 1.3 Verify bootstrap restores only ignored mutable dependency state

## 2. Downstream Loader Build Contract

- [ ] 2.1 Implement strict loader-specific input validation and isolated source preparation
- [ ] 2.2 Build complete BepInEx and MelonLoader output sets into atomic caller-owned directories
- [ ] 2.3 Emit deterministic provenance with source, tool, input, and output hashes
- [ ] 2.4 Add behavioral tests for validation, failure cleanup, and manifest generation

## 3. Workflow Cutover

- [ ] 3.1 Route Lefthook and worktree bootstrap through the pinned environment
- [ ] 3.2 Replace duplicated GitHub Actions tool installers with the canonical Nix gate
- [ ] 3.3 Remove Homebrew, user-path, and sibling-checkout fallbacks from repository guidance

## 4. Acceptance

- [ ] 4.1 Run strict OpenSpec validation and repository formatting
- [ ] 4.2 Verify a clean Core-only worktree bootstrap and complete pre-push gate
- [ ] 4.3 Build one real BepInEx artifact from local game assemblies and inspect provenance
- [ ] 4.4 Commit specification and implementation as atomic Conventional Commits
- [ ] 4.5 Publish the pull request, require clean checks, and archive the accepted change
