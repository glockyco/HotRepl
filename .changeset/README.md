# Changesets

This folder holds [changesets](https://github.com/changesets/changesets) — small markdown files that
record release intent for one or more packages in the `@hotrepl/*` monorepo.

## What goes in a changeset

A changeset declares **which packages change** and **how the version should bump**. The action that
runs on `main` reads these files, opens a "Release packages" PR that bumps versions and updates
per-package `CHANGELOG.md`, and on merge publishes to npm and creates GitHub Releases.

## When to add one

Add a changeset to **any PR that changes a publishable package** (`@hotrepl/protocol`,
`@hotrepl/sdk`, `@hotrepl/mcp`, `@hotrepl/cli`).

Skip changesets for repo-only changes (CI tweaks, docs that don't ship with the npm tarball,
internal `@hotrepl/testing` / `@hotrepl/conformance`).

## How to add one

```bash
bun changeset
```

Pick the packages your PR touches, then pick a bump type per package:

- **patch** — bug fixes, internal refactors, anything backward-compatible
- **minor** — new public API, new typed-command surface, new MCP tool
- **major** — breaking change to a published API, protocol shape, or CLI flag

Write a short summary in the editor that opens. That summary becomes the `CHANGELOG.md` entry, so
write it for the **consumer**, not the diff.

## Bumping behaviour

`updateInternalDependencies: "patch"` — when one package bumps, any sibling that depends on it gets
an automatic patch bump too. So a protocol minor bump cascades to sdk/mcp/cli as patch bumps for
free; you don't need to list them in your changeset.

## Releasing

The maintainer doesn't run `bun changeset publish` by hand. The `changesets/action` workflow opens a
Version PR; merging it triggers the publish + GitHub Release step.
