# HotRepl Site Phase 4 Docs Pass — Design Spec

**Date:** 2026-05-24

## Goal

Update `hotrepl.glockyco.com` so it reflects the Phase 4 HotRepl story: live Unity runtime
inspection, typed commands as the stable automation surface, first-party TypeScript and C# SDKs,
CLI/MCP consumers, and file artifacts as first-class references.

This is a structure and content pass, not a visual redesign. Keep the existing dark, compact
SvelteKit site and improve the information architecture, copy, links, and orientation blocks.

## Research basis

- Developer docs should be organized around developer intent, not internal product structure. Use a
  simple single-context IA with clear task/reference paths. Source: Fern IA guide,
  <https://buildwithfern.com/post/information-architecture-best-practices-documentation>.
- A quickstart should produce one small end-to-end success and then point to next steps. Source: The
  Good Docs Project quickstart template, <https://www.thegooddocsproject.dev/template/quickstart>.
- SvelteKit pages should keep unique titles/descriptions, SSR/prerendered content, and accessible
  route announcements via descriptive page titles. Sources: <https://svelte.dev/docs/kit/seo>,
  <https://svelte.dev/docs/kit/accessibility>.

## Scope

### In scope

- Landing page copy and structure in `site/src/routes/+page.svelte`.
- Landing page server-rendered code snippets in `site/src/routes/+page.server.ts` if needed.
- Protocol page orientation copy in `site/src/routes/protocol/+page.svelte`.
- Top navigation labels/links in `site/src/routes/+layout.svelte`.
- SEO descriptions if page positioning changes.
- Small CSS additions inside existing Svelte files.

### Out of scope

- New routes or Markdown rendering pipeline.
- New dependencies.
- Broad visual redesign, component library work, search, tabs, accordions, or client-side state
  beyond what exists.
- Changing protocol schemas, SDK APIs, docs outside the website, or generated assets.
- Duplicating the full `docs/authoring-commands.md` content into the site.

## Information architecture

HotRepl remains a single-context product site. The homepage should answer three questions quickly:

1. What is HotRepl? A live Unity runtime bridge for eval, typed commands, artifacts, and automation.
2. What should I use? Choose SDK, CLI, MCP, authoring guide, or protocol reference based on task.
3. Where do I go next? Quick links to protocol reference, authoring guide, GitHub, and reference
   consumers.

The protocol page remains a reference page, but gains an orientation block so readers know when to
use it directly versus using SDKs and authoring APIs.

## Landing page design

### Hero

Keep the current compact hero and CTAs. Adjust copy to broaden the value proposition from “runtime
C# REPL” to “live Unity automation.” Mention eval, typed commands, artifacts, SDKs, and agents
without adding a marketing wall of text.

Primary CTA stays `See it work`. Secondary CTAs should include `Protocol` and `GitHub`. The install
strip may continue to show TypeScript/MCP because those are npm/Bun entry points, but the
surrounding page must make C# SDK visibility equally strong.

### Feature cards

Replace the current three cards with intent-oriented cards:

- **Inspect the live runtime** — raw eval on the Unity main thread for diagnosis and repair.
- **Ship typed commands** — schema-validated host operations for exports, tests, and repeatable
  automation.
- **Automate from SDKs and agents** — TypeScript SDK, C# SDK, CLI, and MCP all use the same
  protocol.

### Quickstart

Keep the existing TypeScript, CLI, and MCP examples. Reframe the section with explicit prerequisite
and outcome text:

- Prerequisite: BepInEx or MelonLoader host loaded and listening on loopback.
- Outcome: connect to `ws://127.0.0.1:18590`, evaluate once, then call stable typed commands.
- Next step: author commands or choose an integration path.

If a C# SDK snippet is short and available from existing package conventions, add it as a fourth
quickstart pane or a compact callout. If not, do not invent unverified API syntax; link the C# SDK
path from the chooser instead.

### Choose your path

Add a new section before or replacing the current integration table. It should be a card/grid
section rather than only a table because task cards are easier to scan. Each card should have a
task, entry point, and link:

- TypeScript SDK — app/controller automation — `@hotrepl/sdk`.
- C# SDK — .NET build tools/tests — `HotRepl.Sdk`.
- CLI — shell/local workflows — `@hotrepl/cli`.
- MCP — coding-agent tool catalog — `@hotrepl/mcp`.
- Author typed commands — host-side automation — `docs/authoring-commands.md`.
- Protocol reference — client implementers/debugging — `/protocol/`.

The existing integration table may be removed if the cards fully replace it, or retained only if it
adds information not captured by cards. Avoid redundant table + card content.

### Artifacts callout

Add a small callout explaining the invariant: artifacts are references (`uri`, `path`, `sha256`,
`byteSize`, `finalized`), not bulk payloads. This should link conceptually to typed commands and
protocol descriptors without becoming a full artifact guide.

### Real consumers

Keep the two consumer cards. Update their descriptions to make the Mono/IL2CPP split and
artifact/export roles explicit. Do not claim unpushed downstream commits are public unless they have
been pushed.

## Protocol page design

Before the message families, add a compact orientation block:

- HotRepl v2 is a stable JSON-over-WebSocket protocol.
- Most consumers should start with SDK/CLI/MCP or author typed commands in C#.
- Use this page when implementing a client, debugging frames, or verifying exact message/schema
  shapes.
- Artifacts and command descriptors are represented as JSON-schema-described metadata and artifact
  refs.

Keep the existing single-page, Cmd+F-able reference. Do not split messages into new pages.

## Navigation and SEO

Header links should prioritize task paths:

- `Quickstart` anchors to `/#quickstart`.
- `Author commands` links to GitHub `docs/authoring-commands.md`.
- `Protocol` links to `/protocol/`.
- `GitHub` remains external.

Keep the `Seo` component and unique page titles/descriptions. If the homepage copy changes
materially, update its description to mention typed commands, SDKs, artifacts, and agents.

## Accessibility and performance

- Preserve semantic headings: one `h1`, section `h2`s, card `h3`s.
- Keep links as real anchors; no JS-only navigation.
- Preserve prerendered server output and existing Shiki highlighting.
- Avoid additional client state and dependencies.
- Ensure mobile layout degrades to single-column grids.

## Verification

Minimum verification before completion:

1. `bun run --cwd site build`
2. `dprint check site/src/routes/+page.server.ts site/src/routes/protocol/+page.server.ts site/src/routes/+layout.svelte site/src/routes/+page.svelte site/src/routes/protocol/+page.svelte --allow-no-files`
   if targeted dprint paths are relevant, otherwise explain which files are not dprint-managed.
3. Browser or HTTP smoke check for `/` and `/protocol/` if a dev server is run.

Before branch-level completion, run `lefthook run pre-push --force`.

## Acceptance criteria

- Homepage communicates HotRepl’s Phase 4 position: eval, typed commands, SDKs, CLI/MCP, artifacts,
  and real consumers.
- Developers can choose the right path from the homepage without reading repo internals.
- Authoring guide and protocol reference are clearly linked from the site.
- Protocol page explains when to use the wire reference directly.
- No new dependencies or routes.
- Site build passes.
