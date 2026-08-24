## 1. Retire the worktree workflow

- [x] 1.1 Remove the `bootstrap-worktree` skill, `scripts/bootstrap-worktree.sh`, and `.worktrees/`
      ignore entry.
- [x] 1.2 Remove the `bootstrap` package and application from `flake.nix`, including every generated
      output reference.
- [x] 1.3 Replace worktree setup in `AGENTS.md` with primary-checkout dependency restoration through
      the pinned shell.
- [x] 1.4 Search live repository content for worktree workflow instructions. Remove each live route
      or record why an internal test-only use remains.
- [x] 1.5 Confirm `nix flake check` no longer exposes `.#bootstrap` and still evaluates every
      supported system.

## 2. Preserve accurate setup diagnostics

- [x] 2.1 Extend the existing doctor application to report required repository tools and the
      optional Unity assemblies needed by the BepInEx host build.
- [x] 2.2 Keep doctor observational. Add a check proving it does not install, copy, link, or modify
      dependencies.
- [x] 2.3 Run doctor from the primary checkout with and without optional host assemblies. Confirm
      Core-only setup remains valid in both cases.

## 3. Make command examples executable

- [x] 3.1 Replace every `archive.preflight` example in the HotRepl usage skill with the registered
      `unity.app.info` command and valid empty arguments.
- [x] 3.2 Add a repository check that derives first-party command names from catalog metadata and
      rejects unknown names in marked operational examples.
- [x] 3.3 Prove the check fails on `archive.preflight`, passes on `unity.app.info`, and reports the
      citing file and command.
- [x] 3.4 Wire the command-example check into the narrow skill test and the repository gate.

## 4. Remove duplicated commit policy

- [x] 4.1 Delete `.claude/skills/commit-guidelines/`.
- [x] 4.2 Replace the `AGENTS.md` commit section with the global `commit-policy` route and only
      constraints enforced by `commitlint.config.js`.
- [x] 4.3 Confirm no live HotRepl instruction duplicates global staging, checkpoint, body, amend, or
      push policy.
- [x] 4.4 Confirm a valid repository-specific Conventional Commit passes the existing commit hook.

## 5. Validate and document the cutover

- [x] 5.1 Update live human documentation that names the removed bootstrap route or obsolete command
      examples.
- [x] 5.2 Run the narrow guidance, catalog, doctor, flake, and commit-hook checks.
- [x] 5.3 Run `nix run .#check` and confirm the complete repository gate passes.
- [x] 5.4 Run `openspec validate align-agent-guidance-with-current-workflow --strict`.
- [x] 5.5 Open a fresh OMP session in the repository and confirm `bootstrap-worktree` and
      `commit-guidelines` are absent while the HotRepl usage skill remains discoverable.
