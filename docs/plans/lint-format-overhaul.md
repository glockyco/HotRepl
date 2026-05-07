# Formatting & Linting Overhaul — Execution Plan

## Goal

Replace the existing formatting/linting setup with a modern, fail-fast, zero-suppression-baseline
stack. Local hooks must catch every check that CI catches. No drift between dev machines, CI, and
IDE. No "warnings tolerated for now" escape hatches.

## Tool selection principle

Every tool must earn its place. Each one either:

- replaces multiple older tools (net reduction), **or**
- catches a class of bugs none of the others cover, **or**
- is load-bearing for the local≥CI invariant.

If a tool's value overlaps heavily with another already in the stack, it is not added. We can layer
more analyzers later if a real gap surfaces; we do not pre-commit to noise.

## Decisions (locked)

| Concern                       | Tool                                                                                                            | Why this one                                                                                                                                                                                                                                    |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Cross-language whitespace     | `.editorconfig` (slimmed)                                                                                       | universal, IDE-native                                                                                                                                                                                                                           |
| C# format                     | **CSharpier** (dotnet local tool + MSBuild check)                                                               | opinionated, format-on-save, enforces line length (`dotnet format` does not)                                                                                                                                                                    |
| C# analyzers                  | **NetAnalyzers + Meziantou.Analyzer + xunit.analyzers** (test only)                                             | NetAnalyzers = SDK baseline; Meziantou catches async/perf/IFormatProvider gaps; xunit = test-only. Roslynator and SonarAnalyzer dropped — heavy overlap, finding fatigue without unique signal. Can be layered in later if a real gap surfaces. |
| C# warnings policy            | `TreatWarningsAsErrors=true` **always** (Debug + Release)                                                       | a warning is a build failure on every machine                                                                                                                                                                                                   |
| Python lint + format          | **Ruff** (lint + format)                                                                                        | replaces flake8/isort/black/pyupgrade in one binary                                                                                                                                                                                             |
| Python type check             | **pyright** (strict)                                                                                            | Microsoft-backed, drives Pylance — CI matches IDE                                                                                                                                                                                               |
| Markdown / JSON / YAML / TOML | **dprint**                                                                                                      | one tool covers all four; pinned WASM plugins limit supply chain                                                                                                                                                                                |
| Spell check                   | **typos** (crate-ci)                                                                                            | Rust binary, near-zero false positives                                                                                                                                                                                                          |
| Actions YAML                  | **actionlint**                                                                                                  | catches workflow bugs that otherwise only show as CI red                                                                                                                                                                                        |
| Hooks orchestrator            | **Lefthook**                                                                                                    | parallel, single binary, no Python/Node runtime tax for hook execution                                                                                                                                                                          |
| Commit messages               | **`@commitlint/cli`** + inline Conventional Commits ruleset                                                     | conventional-changelog org, the canonical commitlint                                                                                                                                                                                            |
| Tool version pinning          | dotnet local tool manifest, uv lock, lefthook config, dprint plugin URLs with explicit `@vX.Y.Z`, brew formulas | reproducible                                                                                                                                                                                                                                    |

### Explicitly not adopted

| Tool                             | Why not                                                                                                                                             |
| -------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Roslynator                       | Heavy overlap with NetAnalyzers; defer until a real gap is felt                                                                                     |
| SonarAnalyzer.CSharp             | Heavy overlap; cognitive-complexity is its niche but other rules duplicate                                                                          |
| basedpyright                     | Single-maintainer fork; plain pyright is Microsoft-backed and effectively equivalent for our needs                                                  |
| commitlint-rs                    | Single-maintainer hobby project; `@commitlint/cli` is the canonical, actively-maintained tool                                                       |
| markdownlint                     | dprint covers formatting; 8 md files don't justify a second tool                                                                                    |
| shellcheck                       | No `.sh` files in repo                                                                                                                              |
| reviewdog                        | CI red is sufficient signal; PR-comment annotation is convenience, not load-bearing                                                                 |
| trunk.io / MegaLinter            | Vendor lock / Docker-only; we want pinned local binaries that match CI exactly                                                                      |
| Prettier                         | dprint clears the bar with smaller surface (no node_modules dep tree); contributor-side cost ≈ Prettier given Node already installed for commitlint |
| dotnet format (whitespace/style) | CSharpier supersedes; enforces line length                                                                                                          |
| mypy                             | pyright was chosen for IDE parity; choosing both is duplicate type-checking                                                                         |

## Non-negotiable invariants

- **Local ≥ CI**: every CI check is wired into Lefthook. CI runs the same commands. If CI catches
  it, `lefthook run pre-push` catches it too.
- **No suppression baseline**: zero rules suppressed broadly to "stay green". Targeted, justified
  `[SuppressMessage]` per call site only when semantically required (e.g., catch-all handlers in
  eval boundary).
- **Warnings = errors everywhere**: `TreatWarningsAsErrors` unconditional — Debug, Release, IDE, CI.
  A warning is a build failure on every machine.
- **Pinned versions**: every tool version pinned at repo level; CI uses the pin. No floating
  `@latest`.
- **Atomic commits**: every commit is independently revertible and leaves the repo green under the
  rules installed up to that commit. CI is green at every commit boundary.

## Commit hygiene

All commits in this work follow Conventional Commits (the policy we are installing). Each phase
produces one or more named commits. Subject lines listed under each phase below. No squashing across
phases. Each commit:

- compiles / passes the checks that exist as of that commit
- is independently revertible
- has a body explaining *why*, not *what* (per `commit-guidelines` skill)

The commit-msg hook is installed in Phase 5 — earlier phases use the format voluntarily. Once the
hook is active, all subsequent commits are validated on the way in.

---

## Phase 0 — Workstation prerequisites

**Not committed.** Local machine setup only.

- `brew install lefthook dprint actionlint commitlint` (`commitlint` is the brew formula for
  `@commitlint/cli`)
- `typos` already present.
- `dotnet` 10.x already present. CSharpier is a per-repo local tool (manifest in Phase 3), not
  global.

Acceptance: `lefthook --version`, `dprint --version`, `actionlint --version`,
`commitlint --version`, `typos --version` all succeed.

Status as of plan-write: all installed.

---

## Phase 1 — Foundation: line endings, dprint, typos, slim editorconfig

Four commits. Each leaves repo green.

### Commit 1.1 — `chore(repo): normalize line endings via .gitattributes`

- Replace `.gitattributes` with full `text=auto eol=lf` policy plus language-specific overrides;
  preserve existing DLL LFS rule.
- Run `git add --renormalize .` to fix any tracked CRLF.
- No tool config changes yet.

### Commit 1.2 — `chore(format): adopt dprint for md/json/yaml/toml`

- Add `dprint.json` at repo root with markdown/json/yaml/toml plugins, pinned plugin URLs, repo-wide
  excludes (bin, obj, .venv, caches, lib/, uv.lock).
- Run `dprint fmt` once. Reformat diff in same commit (mechanical).
- Acceptance: `dprint check` → 0.

### Commit 1.3 — `chore(spell): adopt typos for source spell-check`

- Add `_typos.toml` at repo root with identifier-regex allowlist for `Il2Cpp*`, `MelonLoader*`,
  `BepInEx*` and file excludes.
- Run `typos --write-changes`. Spell fixes land in the same commit.
- Acceptance: `typos` → 0.

### Commit 1.4 — `chore(editorconfig): slim to whitespace + naming policy`

- Strip ~80 lines of `csharp_*` layout preferences. Keep:
  - charset, eol, indent, trim/insert-newline
  - per-extension indent overrides (XML/YAML/JSON 2sp)
  - naming conventions (consumed by analyzers)
  - `csharp_using_directive_placement` (CSharpier doesn't enforce)
  - `dotnet_sort_system_directives_first`
  - placeholder section for analyzer severity overrides (filled in Phase 4)
- No reformat yet. CSharpier reformats in Phase 3.

---

## Phase 2 — Python: ruff format + expanded rules + pyright

### Commit 2.1 — `chore(python): centralize tool config at repo root`

- Add root `pyproject.toml` with `[tool.ruff]` and `[tool.pyright]` sections only (no `[project]`).
  Covers `client/` and `scripts/`.
- Remove `[tool.ruff]` and `[tool.mypy]` from `client/pyproject.toml`.
- Replace `mypy` with `pyright` in `client/[project.optional-dependencies].dev`.
- Update `uv.lock` accordingly.

### Commit 2.2 — `style(python): apply ruff format and expanded lint`

- Configure expanded ruff lint rule set (E/W/F/I/N/UP/B/C4/SIM/RET/ARG/PIE/
  PT/PTH/TID/ICN/PYI/ASYNC/S/A/T20/RUF/PERF/FURB/LOG/G/TRY/TC/ERA/PL/D) with per-file ignores for
  `tests/**`, CLI entrypoints, `scripts/**`.
- Run `ruff format` + `ruff check --fix --unsafe-fixes`.
- Manually resolve any remaining lint findings — **no broad ignores added beyond per-file-ignores
  above**.
- Acceptance: `ruff check` → 0; `ruff format --check` → 0.

### Commit 2.3 — `chore(python): adopt pyright strict, drop mypy`

- pyright config: `typeCheckingMode = "strict"`, include `client/src`, `client/tests`, `scripts`, py
  3.11 baseline, `reportImplicitOverride = "error"`, `reportMissingTypeStubs = "warning"`.
- Run `uvx pyright`. Fix every error. No `# pyright: ignore` baseline-style suppression — only
  justified per-line ignores with a brief reason comment.
- Drop `mypy` from CI step (replaced with pyright in Phase 6).
- Remove `.mypy_cache/` from disk (already in `.gitignore`).
- Acceptance: `pyright` → 0 errors, 0 warnings.

---

## Phase 3 — C#: CSharpier reformat + dotnet tool manifest

### Commit 3.1 — `chore(csharp): pin CSharpier as dotnet local tool`

- `dotnet new tool-manifest` → `.config/dotnet-tools.json`.
- `dotnet tool install csharpier` (latest stable, pinned in manifest).
- Add `.csharpierrc.json` at repo root: `{ "lineWidth": 100 }`. (Keep existing 4-space indent —
  CSharpier reads `.editorconfig`.)
- Acceptance: `dotnet csharpier check src/ tests/` reports current unformatted state (we don't fail
  CI yet — that's 3.2).

### Commit 3.2 — `style(csharp): apply CSharpier across solution`

- `dotnet csharpier format src/ tests/`. One mechanical reformat commit. No behavior changes.
- Verify build and tests still green.
- Acceptance: `dotnet csharpier check` → 0.

---

## Phase 4 — C#: analyzer pack + TreatWarningsAsErrors

This is the largest phase. Splits into multiple commits because the analyzer sweep produces many
findings, each fixed properly (not suppressed).

### Commit 4.1 — `chore(csharp): centralize package versions via CPM`

- Add `Directory.Packages.props` at repo root with `ManagePackageVersionsCentrally=true`.
- Move every existing `<PackageReference Version="...">` from csprojs into `<PackageVersion>`
  entries.
- csprojs keep `<PackageReference Include="..." />` only.

### Commit 4.2 — `chore(csharp): add Directory.Build.props with strict policy`

- Add `Directory.Build.props` at repo root:
  - `LangVersion=latest`
  - `Nullable=enable`
  - `AnalysisLevel=latest-recommended`
  - `AnalysisMode=All`
  - `EnforceCodeStyleInBuild=true`
  - `TreatWarningsAsErrors=true` (unconditional)
  - `GenerateDocumentationFile=true`
  - `NoWarn=CS1591` (XML doc on internal — handled by analyzer severity instead)
- Add NetAnalyzers + Meziantou.Analyzer references (xunit.analyzers added to test csproj only via
  `Directory.Build.props` conditional or test-specific props).
- **Expected**: build will explode with findings. The next commits fix them.

### Commit 4.3 — `refactor(core): resolve analyzer findings in HotRepl.Core`

Iterate file-by-file. For each finding category:

- **Nullability (CS86xx)**: annotate properly. No `!` null-forgiving unless documented. No
  `#nullable disable`.
- **CA1031 catch general exception**: where load-bearing (eval boundary, REPL must not crash host),
  suppress per-call-site with `[SuppressMessage("Design", "CA1031:...", Justification = "...")]` and
  a one-line comment. Otherwise, narrow the catch.
- **CA2007 ConfigureAwait**: this is plugin/host context — suppress globally via `.editorconfig` for
  `[*.cs]` with a documented reason (no SynchronizationContext to capture in plugin host).
- **CA1303 localize**: REPL is dev tool — suppress globally with reason.
- **MA0004 Meziantou ConfigureAwait**: same family as CA2007.
- Every other finding: **fix the code**.

If too large for one commit, split: `refactor(core/protocol):`, `refactor(core/control):`, etc. Each
split must build green.

### Commit 4.4 — `refactor(evaluator): resolve analyzer findings`

Same treatment for `HotRepl.Evaluator.MonoCSharp` and `HotRepl.Evaluator.Roslyn`.

### Commit 4.5 — `refactor(tests): resolve analyzer + xunit findings`

- xunit.analyzers package added to `tests/HotRepl.Tests.csproj`.
- xUnit2xxx (Assert correctness), CA1707 (test method naming), CA2007 in tests context — fix or
  document.

### Commit 4.6 — `chore(csharp): wire CSharpier MSBuild check into builds`

- Add `<PackageVersion Include="CSharpier.MsBuild" Version="..." />` to `Directory.Packages.props`.
- Add `<PackageReference Include="CSharpier.MsBuild" PrivateAssets="all" />` to
  `Directory.Build.props`.
- `dotnet build` itself fails on unformatted C#.
- Acceptance: `dotnet build` → 0 warnings, 0 errors across all CI projects.

### Commit 4.7 — `refactor(host): resolve analyzer findings in non-CI projects`

- BepInEx/MelonLoader/Helpers projects don't build in CI but must not rot.
- Build them locally with the new policy. Fix findings same as 4.3.

---

## Phase 5 — Lefthook + commitlint

### Commit 5.1 — `chore(hooks): adopt lefthook with commitlint and full local gate`

- Add `lefthook.yml` at repo root.
- Add `commitlint.config.js` with **inline Conventional Commits ruleset** (no `extends` — avoids
  Node module resolution; the ruleset is small, stable, and copied verbatim from
  `@commitlint/config-conventional`). Type enum:
  `feat, fix, refactor, perf, style, test, docs, build, ci,
  chore, revert`.
- `lefthook install` adds the git hooks.
- `pre-commit`: format on staged files (CSharpier, ruff format, ruff check --fix, dprint fmt,
  typos), `stage_fixed: true`.
- `pre-push`: full repo gate — exactly what CI runs.
- `commit-msg`: commitlint validates the message.

`lefthook.yml` shape:

```yaml
pre-commit:
  parallel: true
  commands:
    csharpier:
      glob: "*.cs"
      run: dotnet csharpier format {staged_files}
      stage_fixed: true
    ruff-fix:
      glob: "*.py"
      run: uvx ruff check --fix {staged_files}
      stage_fixed: true
    ruff-format:
      glob: "*.py"
      run: uvx ruff format {staged_files}
      stage_fixed: true
    dprint:
      glob: "*.{md,json,yml,yaml,toml}"
      run: dprint fmt {staged_files}
      stage_fixed: true
    typos:
      run: typos {staged_files}

commit-msg:
  commands:
    commitlint:
      run: commitlint --edit {1}

pre-push:
  parallel: true
  commands:
    csharpier-check:
      run: dotnet csharpier check src/ tests/
    ruff-check:
      run: uvx ruff check
    ruff-format-check:
      run: uvx ruff format --check
    pyright:
      run: uvx pyright
    dprint-check:
      run: dprint check
    typos:
      run: typos
    actionlint:
      run: actionlint
    dotnet-build:
      run: dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
    dotnet-test:
      run: dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q
```

Acceptance:

- `lefthook run pre-commit --all-files` → 0
- `lefthook run pre-push` → 0
- A test commit with non-conformant message is rejected by `commit-msg`.

---

## Phase 6 — CI restructure to mirror Lefthook

### Commit 6.1 — `ci: restructure pipeline to mirror local hook gate`

Replace `.github/workflows/ci.yml` with parallel jobs:

1. **`format-and-lint`** — checkout, install dotnet/uv/dprint/typos/ actionlint, run:
   `dprint check`, `typos`, `actionlint`, `dotnet csharpier check`, `uvx ruff check`,
   `uvx ruff format --check`.
2. **`csharp`** — restore + build Core/Evaluator.MonoCSharp/ Evaluator.Roslyn/Tests with
   warnings-as-errors (already implied by props). `dotnet test`.
3. **`python`** — `uvx pyright`, `pytest -v --tb=short`.
4. **`commit-lint`** — `pull_request` only: validate every commit in the PR range with commitlint.
5. **`hooks-parity`** — install lefthook, run `lefthook run pre-push`. Guarantees local and CI
   cannot drift.

Pin every action by SHA. Pin tool versions:

- dotnet 10.x via `setup-dotnet`
- python 3.11 via `setup-python`
- uv via `astral-sh/setup-uv`
- dprint via direct install (`curl | sh` with sha verification, or `dprint/check-action`)
- typos via `crate-ci/typos`
- actionlint via direct install
- lefthook via direct install (single binary)
- commitlint via brew/npm

Acceptance: PR run is green; hooks-parity job proves local ≥ CI.

---

## Phase 7 — Documentation

### Commit 7.1 — `docs: document the toolchain in AGENTS.md and README`

- Update `AGENTS.md` "Build & Test" section with new commands.
- Add a "Toolchain" section: warnings = errors, no suppression baseline, local mirrors CI.
- Bootstrap one-liner:
  `brew install lefthook dprint actionlint commitlint typos && dotnet tool restore && lefthook install`.
- Update README contributor section similarly.

---

## Acceptance criteria (whole plan)

After all commits land:

- `lefthook run pre-commit --all-files` → 0
- `lefthook run pre-push` → 0
- `dotnet build` from clean: 0 warnings, 0 errors, Debug and Release
- `dotnet test` from clean: all green
- CI: all jobs green
- A commit with `update stuff` as message is rejected by commit-msg hook
- A new file with a typo, unformatted code, or analyzer warning fails `pre-commit` locally before
  commit
- A `--no-verify` push is still blocked by CI before merge
- Zero broad analyzer suppressions; only the three globally-suppressed rules (CA2007, CA1303,
  MA0004) — each with a one-line `# Reason:` comment in `.editorconfig` — plus call-site
  `[SuppressMessage]` with documented justifications

## Risks and mitigations

| Risk                                                                                                   | Mitigation                                                                                                                                                                 |
| ------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Phase 4 sweep is open-ended                                                                            | Split by directory. Each commit must build green. If a finding cannot be fixed without behavior change, file via `bd` and SuppressMessage with the bd id in justification. |
| BepInEx/MelonLoader projects don't build in CI; analyzer drift possible                                | Phase 4.7 fixes them locally. Hooks-parity job calls `lefthook run pre-push` which builds Core only — same as CI. Host projects checked manually before each release.      |
| ILRepack interaction with analyzer-injected attributes                                                 | CSharpier.MsBuild and analyzer packages are `PrivateAssets="all"`, no runtime artifacts. Verify a clean BepInEx build still ILRepacks correctly after Phase 4.6.           |
| Mono.CSharp evaluates user code as C# 7 — does enabling `LangVersion=latest` for source break runtime? | No. `LangVersion` controls compilation of repo source. Mono.CSharp at runtime is independent.                                                                              |
| Ruff/pyright sweep may surface real bugs                                                               | Good. Fix them.                                                                                                                                                            |
| commitlint-rs unmaintained — what if `@commitlint/cli` brew formula breaks?                            | Brew formula is stable; conventional-changelog is widely-used (17k stars). Fallback: `npx @commitlint/cli` if Node is available.                                           |
| dprint plugin URLs change or get yanked                                                                | Plugins are pinned at specific versions in `dprint.json`. dprint caches WASM blobs in user dir — once fetched, offline-stable.                                             |
