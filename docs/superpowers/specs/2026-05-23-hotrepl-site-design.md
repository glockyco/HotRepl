# HotRepl Site — Design Spec

**Date:** 2026-05-23

## Goal

Build `hotrepl.glockyco.com`, a public documentation website for HotRepl covering a landing page and
a fully auto-validated protocol reference. Simultaneously:

1. Upgrade `@sinclair/typebox` 0.34.x → `typebox` 1.x across `packages/protocol`.
2. Migrate all message types in `packages/protocol` from plain TypeScript interfaces to TypeBox
   schemas.
3. Add client-message schemas that don't exist yet.
4. Expand the schema export script to cover every wire type.
5. Integrate the site into the personal website project list with a real `liveUrl`.

---

## Scope

| Repo                          | Changes                                              |
| ----------------------------- | ---------------------------------------------------- |
| `~/Projects/HotRepl`          | TypeBox upgrade, full TypeBox migration, new `site/` |
| `~/Projects/personal-website` | New `hotrepl` project entry, screenshot capture      |

---

## Part 1 — TypeBox upgrade (0.34.x → 1.x)

### Package rename

TypeBox 1.x is published as `typebox` (unscoped). `@sinclair/typebox` is frozen at 0.34.x as a
legacy branch.

In `packages/protocol/package.json`:

```diff
- "@sinclair/typebox": "^0.34.0"
+ "typebox": "^1.0.0"
```

### Import changes

| 0.34.x                                                      | 1.x                                           |
| ----------------------------------------------------------- | --------------------------------------------- |
| `import { type Static, Type } from "@sinclair/typebox"`     | `import Type, { type Static } from "typebox"` |
| `import { Value } from "@sinclair/typebox/value"`           | `import Value from "typebox/value"`           |
| `import { TypeCompiler } from "@sinclair/typebox/compiler"` | `import { Compile } from "typebox/compile"`   |

> **Verify:** Confirm the exact named vs. default export shape from `typebox@1.x` type definitions
> before implementation. The migration guide shows `import Type from 'typebox'` (default).
> `Static<T>` is not listed as removed and should remain as a named type export.

### Breaking changes — impact assessment for HotRepl

| Change                                                                               | Impact                                                    |
| ------------------------------------------------------------------------------------ | --------------------------------------------------------- |
| Symbols (`Kind`, `ReadonlyKind`, `OptionalKind`) removed; `~kind` etc. replaces them | **None** — not used directly in HotRepl code              |
| `Type.Date()`, `Type.Uint8Array()` removed                                           | **None** — not used                                       |
| `Type.Recursive` removed (→ `Type.Cyclic`)                                           | **None** — not used                                       |
| `Type.RegExp` removed (→ `Type.String({ pattern })`)                                 | **None** — not used                                       |
| `Type.Composite` removed (→ `Type.Interface` / `Type.Evaluate`)                      | **None** — not used                                       |
| `Type.Transform` renamed to `Type.Codec`                                             | **None** — not used                                       |
| `Type.Const` removed (→ `Type.Script`)                                               | **None** — not used                                       |
| `TypeCompiler` → `Compile`; `TypeCheck` → `Validator`                                | **None** — HotRepl uses `Value.Check()`, not the compiler |
| References passed via Context object, not array                                      | **None** — HotRepl doesn't use `$ref` references          |
| `TypeGuard.*` moved to `Type.Is*()`                                                  | **None** — not used                                       |
| `Value.Errors()` returns `Array` (was `IterableIterator`)                            | **Simplification** — spread syntax no longer needed       |
| `Value.Cast()` renamed to `Value.Repair()`                                           | **None** — not used                                       |
| `FormatRegistry` moved to `Format` submodule                                         | **None** — not used                                       |
| `TypeRegistry` removed (→ `Type.Base`)                                               | **None** — not used                                       |
| `SetErrorFunction` removed (→ `Locale`)                                              | **None** — not used                                       |
| ESM-only package                                                                     | **None** — HotRepl already uses `"type": "module"`        |

### JSON Schema output in 1.x

In 0.34.x, TypeBox stored metadata in enumerable Symbol-keyed properties (`[Kind]`), requiring
`Type.Strict()` to strip them before `JSON.stringify()`. In 1.x, internal properties (`~kind`,
`~readonly`, `~optional`) are **non-enumerable**, so `JSON.stringify(schema)` already produces
clean, valid JSON Schema directly. `Type.Strict()` may be gone or a no-op; verify and omit if
unnecessary.

### Affected files in `packages/protocol`

- `package.json` — dependency name
- `src/handshake.ts` — one import line
- `src/messages.ts` — one import line (added as part of Part 2)
- `test/handshake.test.ts` — one import line (`@sinclair/typebox/value` → `typebox/value`)

---

## Part 2 — Full TypeBox migration in `packages/protocol/src/messages.ts`

### Why

- Only `handshake.ts` currently has TypeBox schemas; all other protocol messages are plain
  interfaces.
- Without schemas, `export-schemas.ts` can only export the handshake, and the docs site cannot
  validate examples at build time.
- Client-sent message shapes currently only exist in the SDK's `RuntimeRequest` union — not in the
  protocol package at all.

### Shared types

These currently exist as plain interfaces and must become TypeBox schemas. Schema names follow the
existing `handshake.ts` convention (`XxxSchema` suffix).

```typescript
import Type, { type Static } from "typebox";
import { ERROR_KINDS } from "./error-kinds";
import { MESSAGE_TYPES } from "./message-types";

// JsonObject: opaque JSON object (used for command args and inline schemas)
export const JsonObjectSchema = Type.Record(Type.String(), Type.Unknown());
export type JsonObject = Static<typeof JsonObjectSchema>;

// Error envelope (was HotReplErrorEnvelope interface)
export const ErrorEnvelopeSchema = Type.Object(
  {
    kind: Type.Union(ERROR_KINDS.map((k) => Type.Literal(k))),
    code: Type.String(),
    message: Type.String(),
    retryable: Type.Boolean(),
    details: Type.Optional(Type.Unknown()),
  },
  { additionalProperties: false },
);
export type HotReplErrorEnvelope = Static<typeof ErrorEnvelopeSchema>;

// ArtifactRef
export const ArtifactRefSchema = Type.Object(
  {
    uri: Type.String(),
    path: Type.Optional(Type.String()),
    sha256: Type.String(),
    byteSize: Type.Number(),
    contentType: Type.String(),
    finalized: Type.Boolean(),
  },
  { additionalProperties: false },
);
export type ArtifactRef = Static<typeof ArtifactRefSchema>;

// CommandSummary
export const CommandSummarySchema = Type.Object(
  {
    name: Type.String(),
    majorVersion: Type.Number(),
    kind: Type.Union([Type.Literal("sync"), Type.Literal("job")]),
    mutatesState: Type.Boolean(),
  },
  { additionalProperties: false },
);
export type CommandSummary = Static<typeof CommandSummarySchema>;

// CommandDescriptor (extends CommandSummary — inline, not using Intersect to keep
// schema output flat and readable)
export const CommandDescriptorSchema = Type.Object(
  {
    name: Type.String(),
    majorVersion: Type.Number(),
    kind: Type.Union([Type.Literal("sync"), Type.Literal("job")]),
    mutatesState: Type.Boolean(),
    inputSchema: JsonObjectSchema,
    outputSchema: JsonObjectSchema,
    artifactsSchema: JsonObjectSchema,
    cancellation: Type.Optional(Type.String()),
  },
  { additionalProperties: false },
);
export type CommandDescriptor = Static<typeof CommandDescriptorSchema>;

// JournalEntry
export const JournalEntrySchema = Type.Object(
  {
    id: Type.String(),
    kind: Type.Union([Type.Literal("eval"), Type.Literal("command")]),
    name: Type.Optional(Type.String()),
    code: Type.Optional(Type.String()),
    success: Type.Boolean(),
    durationMs: Type.Number(),
    errorKind: Type.Optional(Type.Union(ERROR_KINDS.map((k) => Type.Literal(k)))),
    timestamp: Type.String(),
  },
  { additionalProperties: false },
);
export type JournalEntry = Static<typeof JournalEntrySchema>;
```

> **`ERROR_KINDS` union pattern:** `Type.Union(ERROR_KINDS.map(k => Type.Literal(k)))` constructs
> the union at module load time from the existing `as const` array. If `ERROR_KINDS` changes, both
> the `ErrorKind` type and the TypeBox schema update automatically.

> **`CommandDescriptor` inline expansion:** `CommandDescriptor` currently uses
> `extends CommandSummary` (TypeScript interface inheritance). Rather than using
> `Type.Intersect([CommandSummarySchema, ...])` — which produces an `anyOf`/`allOf` JSON Schema that
> is harder to read — inline all fields explicitly. This is deliberate duplication for schema
> clarity.

### Client-sent messages (new — currently absent from `messages.ts`)

The authoritative source for these shapes is `RuntimeRequest` in `packages/sdk/src/session.ts`. The
TypeBox schemas must match exactly.

```typescript
export const EvalMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.eval),
    id: Type.String(),
    code: Type.String(),
    timeoutMs: Type.Optional(Type.Number()),
  },
  { additionalProperties: false },
);
export type EvalMessage = Static<typeof EvalMessageSchema>;

export const CompleteMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.complete),
    id: Type.String(),
    code: Type.String(),
    cursor: Type.Optional(Type.Number()),
  },
  { additionalProperties: false },
);
export type CompleteMessage = Static<typeof CompleteMessageSchema>;

export const ResetMessageSchema = Type.Object(
  { type: Type.Literal(MESSAGE_TYPES.reset), id: Type.String() },
  { additionalProperties: false },
);
export type ResetMessage = Static<typeof ResetMessageSchema>;

export const SubscribeMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.subscribe),
    id: Type.String(),
    code: Type.String(),
    intervalFrames: Type.Optional(Type.Number()),
    limit: Type.Optional(Type.Number()),
  },
  { additionalProperties: false },
);
export type SubscribeMessage = Static<typeof SubscribeMessageSchema>;

// cancel is in MESSAGE_TYPES but not yet in RuntimeRequest.
// Define the schema for documentation completeness; the SDK does not currently send it.
export const CancelMessageSchema = Type.Object(
  { type: Type.Literal(MESSAGE_TYPES.cancel), id: Type.String(), targetId: Type.String() },
  { additionalProperties: false },
);
export type CancelMessage = Static<typeof CancelMessageSchema>;

export const CommandsListMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.commandsList),
    id: Type.String(),
    since: Type.Optional(Type.String()),
  },
  { additionalProperties: false },
);
export type CommandsListMessage = Static<typeof CommandsListMessageSchema>;

export const CommandDescribeMessageSchema = Type.Object(
  { type: Type.Literal(MESSAGE_TYPES.commandDescribe), id: Type.String(), name: Type.String() },
  { additionalProperties: false },
);
export type CommandDescribeMessage = Static<typeof CommandDescribeMessageSchema>;

export const CommandCallMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.commandCall),
    id: Type.String(),
    name: Type.String(),
    args: Type.Unknown(),
    timeoutMs: Type.Optional(Type.Number()),
  },
  { additionalProperties: false },
);
export type CommandCallMessage = Static<typeof CommandCallMessageSchema>;

export const JobStatusMessageSchema = Type.Object(
  { type: Type.Literal(MESSAGE_TYPES.jobStatus), id: Type.String(), jobId: Type.String() },
  { additionalProperties: false },
);
export type JobStatusMessage = Static<typeof JobStatusMessageSchema>;

export const JobCancelMessageSchema = Type.Object(
  { type: Type.Literal(MESSAGE_TYPES.jobCancel), id: Type.String(), jobId: Type.String() },
  { additionalProperties: false },
);
export type JobCancelMessage = Static<typeof JobCancelMessageSchema>;

export const JournalQueryMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.journalQuery),
    id: Type.String(),
    kind: Type.Optional(Type.Union([Type.Literal("eval"), Type.Literal("command")])),
    limit: Type.Optional(Type.Number()),
  },
  { additionalProperties: false },
);
export type JournalQueryMessage = Static<typeof JournalQueryMessageSchema>;

export type ClientMessage =
  | EvalMessage
  | CompleteMessage
  | ResetMessage
  | SubscribeMessage
  | CancelMessage
  | CommandsListMessage
  | CommandDescribeMessage
  | CommandCallMessage
  | JobStatusMessage
  | JobCancelMessage
  | JournalQueryMessage;
```

### Server-sent messages (convert existing interfaces)

Every `export interface XxxMessage` in `messages.ts` becomes
`export const XxxMessageSchema = Type.Object(...)` followed by
`export type XxxMessage = Static<typeof XxxMessageSchema>`. The type aliases are backward-compatible
— `Static<typeof XxxMessageSchema>` produces the same TypeScript type as the original interface. No
callers outside `packages/protocol` need changes for this part.

Representative conversions (the pattern applies to all 17 remaining server messages):

```typescript
export const EvalResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.evalResult),
    id: Type.String(),
    hasValue: Type.Boolean(),
    value: Type.Optional(Type.Unknown()),
    valueType: Type.Optional(Type.String()),
    stdout: Type.Optional(Type.String()),
    durationMs: Type.Number(),
  },
  { additionalProperties: false },
);
export type EvalResultMessage = Static<typeof EvalResultMessageSchema>;

export const EvalErrorMessageSchema = Type.Object(
  { type: Type.Literal(MESSAGE_TYPES.evalError), id: Type.String(), error: ErrorEnvelopeSchema },
  { additionalProperties: false },
);
export type EvalErrorMessage = Static<typeof EvalErrorMessageSchema>;

export const CommandResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.commandResult),
    id: Type.String(),
    status: Type.Union([Type.Literal("ok"), Type.Literal("failed")]),
    output: Type.Optional(Type.Unknown()),
    artifacts: Type.Record(Type.String(), ArtifactRefSchema),
    error: Type.Optional(ErrorEnvelopeSchema),
    durationMs: Type.Number(),
  },
  { additionalProperties: false },
);
export type CommandResultMessage = Static<typeof CommandResultMessageSchema>;

// ... all remaining 14 server-message schemas follow the same pattern.
// Exact field lists are in control-plane-protocol.md and messages.ts.
```

### `ServerMessage` union

```typescript
export const ServerMessageSchema = Type.Union([
  HandshakeMessageSchema,
  EvalResultMessageSchema,
  EvalErrorMessageSchema,
  CompleteResultMessageSchema,
  ResetResultMessageSchema,
  SubscribeResultMessageSchema,
  SubscribeErrorMessageSchema,
  SessionEvictedMessageSchema,
  ProtocolErrorMessageSchema,
  CommandsListResultMessageSchema,
  CommandDescribeResultMessageSchema,
  CommandResultMessageSchema,
  JobAcceptedMessageSchema,
  JobStatusResultMessageSchema,
  JobResultMessageSchema,
  JobCancelResultMessageSchema,
  JournalQueryResultMessageSchema,
]);
export type ServerMessage = Static<typeof ServerMessageSchema>;
```

### `index.ts`

The existing `export * from "./messages"` barrel re-exports everything automatically. No changes
needed to `index.ts` for the new schemas — they come out of `messages.ts`.

---

## Part 3 — Schema export script (`packages/protocol/scripts/export-schemas.ts`)

Rewrite to export one `.schema.json` per wire type for every message schema — client and server —
plus shared types.

File naming: `{wire-type-discriminant}.schema.json` (e.g., `eval_result.schema.json`). Shared types
use their type name lowercased: `error-envelope.schema.json`, `artifact-ref.schema.json`,
`command-descriptor.schema.json`, `journal-entry.schema.json`.

```typescript
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  ArtifactRefSchema,
  CommandDescriptorSchema,
  // ... all schemas
  ErrorEnvelopeSchema,
  EvalErrorMessageSchema,
  EvalMessageSchema,
  EvalResultMessageSchema,
  HandshakeMessageSchema,
  JournalEntrySchema,
} from "../src";

const schemaDir = join(dirname(fileURLToPath(import.meta.url)), "..", "schemas");
await mkdir(schemaDir, { recursive: true });

const entries: Array<{ file: string; schema: unknown }> = [
  { file: "handshake.schema.json", schema: HandshakeMessageSchema },
  { file: "eval.schema.json", schema: EvalMessageSchema },
  { file: "eval_result.schema.json", schema: EvalResultMessageSchema },
  // ... one entry per schema
  { file: "error-envelope.schema.json", schema: ErrorEnvelopeSchema },
  { file: "artifact-ref.schema.json", schema: ArtifactRefSchema },
  { file: "command-descriptor.schema.json", schema: CommandDescriptorSchema },
  { file: "journal-entry.schema.json", schema: JournalEntrySchema },
];

for (const { file, schema } of entries) {
  // In TypeBox 1.x, ~kind/~readonly/~optional are non-enumerable, so
  // JSON.stringify already produces clean JSON Schema. Wrap in Type.Strict()
  // only if verification shows it is still needed.
  await writeFile(join(schemaDir, file), `${JSON.stringify(schema, null, 2)}\n`);
}
```

### Tests (`packages/protocol/test/messages.test.ts`)

Add one `Value.Check(schema, example)` assertion per schema using the same pattern as
`handshake.test.ts`. Every schema — client, server, and shared — must have at least one positive
test that passes `Value.Check`. Use minimal-valid examples; focus on required-field coverage and a
representative optional-field case.

```typescript
import { describe, expect, test } from "bun:test";
import Value from "typebox/value";
import { ErrorEnvelopeSchema, EvalMessageSchema, EvalResultMessageSchema /* ... */ } from "../src";

describe("message schemas", () => {
  test("eval message validates", () => {
    expect(Value.Check(EvalMessageSchema, {
      type: "eval",
      id: "e1",
      code: "1 + 1",
    })).toBe(true);
  });

  test("eval_result validates (no value)", () => {
    expect(Value.Check(EvalResultMessageSchema, {
      type: "eval_result",
      id: "e1",
      hasValue: false,
      durationMs: 3,
    })).toBe(true);
  });

  test("eval_result validates (with value)", () => {
    expect(Value.Check(EvalResultMessageSchema, {
      type: "eval_result",
      id: "e1",
      hasValue: true,
      value: "42",
      valueType: "System.Int32",
      durationMs: 3,
    })).toBe(true);
  });

  // ... one block per schema
});
```

---

## Part 4 — HotRepl site (`site/`)

### Workspace setup

Add `site` to `workspaces` in root `package.json`:

```json
"workspaces": ["packages/*", "site"]
```

### Tech stack

| Tool                                    | Notes                                            |
| --------------------------------------- | ------------------------------------------------ |
| SvelteKit 2.x + Svelte 5.x              | Same as `ardenfall-compendium/site/`             |
| `@sveltejs/adapter-cloudflare`          | Static + Worker on Cloudflare                    |
| Tailwind CSS v4 via `@tailwindcss/vite` | Same as reference projects                       |
| Shiki v3.x                              | Syntax highlighting; runs at prerender time only |
| `@hotrepl/protocol` (workspace)         | Imported for schemas and `MESSAGE_TYPES`         |
| `typebox` v1.x (workspace, transitive)  | Needed for `TSchema` type in `protocol.ts`       |
| Wrangler v4.x                           | Deploy                                           |

No Shadcn-UI, bits-ui, or other component libraries. Custom components only.

### File structure

```
site/
  package.json
  tsconfig.json
  svelte.config.js
  vite.config.ts
  wrangler.toml
  src/
    app.html
    app.css
    routes/
      +layout.svelte            # top nav + footer
      +page.svelte              # landing page
      +page.ts                  # prerender = true; load() for quickstart highlight
      protocol/
        +page.svelte            # protocol reference
        +page.ts                # prerender = true; load() validates + highlights all examples
    lib/
      data/
        protocol.ts             # families[], sharedTypes[], validateAllExamples()
      components/
        MessageCard.svelte
        ProtocolNav.svelte
```

### `site/package.json`

```json
{
  "name": "@hotrepl/site",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite dev",
    "build": "vite build",
    "preview": "vite preview",
    "check": "svelte-kit sync && svelte-check --tsconfig ./tsconfig.json",
    "cf-deploy": "wrangler deploy"
  },
  "devDependencies": {
    "@hotrepl/protocol": "workspace:*",
    "@sveltejs/adapter-cloudflare": "latest",
    "@sveltejs/kit": "^2.0.0",
    "@sveltejs/vite-plugin-svelte": "latest",
    "@tailwindcss/vite": "^4.0.0",
    "shiki": "^3.0.0",
    "svelte": "^5.0.0",
    "svelte-check": "latest",
    "tailwindcss": "^4.0.0",
    "typebox": "^1.0.0",
    "vite": "latest",
    "wrangler": "^4.0.0"
  }
}
```

### `wrangler.toml`

```toml
name = "hotrepl-site"
main = ".svelte-kit/cloudflare/_worker.js"
compatibility_date = "2026-05-23"
compatibility_flags = ["nodejs_compat"]
workers_dev = false

[[routes]]
pattern = "hotrepl.glockyco.com"
custom_domain = true

[assets]
directory = ".svelte-kit/cloudflare"
binding = "ASSETS"
```

### `svelte.config.js`

```javascript
import adapter from "@sveltejs/adapter-cloudflare";
import { vitePreprocess } from "@sveltejs/vite-plugin-svelte";

const config = {
  preprocess: vitePreprocess(),
  kit: {
    adapter: adapter({}),
    alias: { $lib: "src/lib" },
  },
};
export default config;
```

### `vite.config.ts`

```typescript
import { sveltekit } from "@sveltejs/vite-plugin-svelte";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [tailwindcss(), sveltekit()],
  // @hotrepl/protocol exports raw TypeScript ("./src/index.ts").
  // Vite must not pre-bundle it through esbuild before SvelteKit's
  // vitePreprocess/TypeScript pipeline can handle it.
  optimizeDeps: {
    exclude: ["@hotrepl/protocol"],
  },
});
```

### Design system (`src/app.css`)

Dark-only. No `.dark` class needed; all tokens on `:root`. Uses Tailwind v4's
`@import "tailwindcss"` + `@theme inline {}` to expose custom tokens as Tailwind utilities.

```css
@import "tailwindcss";

:root {
  --bg:         oklch(0.11 0.01 240);
  --surface:    oklch(0.16 0.01 240);
  --surface-2:  oklch(0.20 0.015 240);
  --border:     oklch(0.28 0.02 240);
  --text:       oklch(0.93 0.01 240);
  --muted:      oklch(0.58 0.02 240);
  --accent:     oklch(0.75 0.18 45);        /* warm orange */
  --accent-dim: oklch(0.75 0.18 45 / 15%);
  --badge-cs:   oklch(0.75 0.18 45);        /* CLIENT→SERVER */
  --badge-sc:   oklch(0.65 0.15 220);       /* SERVER→CLIENT */
  --code-bg:    oklch(0.13 0.01 240);
  --radius:     6px;
}

@theme inline {
  --color-bg:         var(--bg);
  --color-surface:    var(--surface);
  --color-surface-2:  var(--surface-2);
  --color-border:     var(--border);
  --color-text:       var(--text);
  --color-muted:      var(--muted);
  --color-accent:     var(--accent);
  --color-accent-dim: var(--accent-dim);
  --color-badge-cs:   var(--badge-cs);
  --color-badge-sc:   var(--badge-sc);
  --color-code-bg:    var(--code-bg);
}

body {
  background: var(--bg);
  color: var(--text);
  font-family: system-ui, -apple-system, sans-serif;
}
```

### `src/app.html`

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    %sveltekit.head%
  </head>
  <body>
    %sveltekit.body%
  </body>
</html>
```

No `class="dark"` needed — the site is dark-only and `app.css` uses `:root` directly.

### Landing page (`src/routes/+page.svelte`)

Content in order:

**1. Hero** (must be visually complete within 675px height at 1200px width — this is what the
personal website screenshot script captures)

- Headline: `HotRepl` (large, bold, accent-colored)
- Tagline: `Runtime C# REPL and typed command bridge for Unity games`
- Body:
  `Embed in any Unity game via BepInEx or MelonLoader. Inspect and automate a running game from your terminal, scripts, or AI agents — without rebuilding.`
- CTAs: `[Protocol Reference →]` (internal `/protocol/`) and `[GitHub]` (external
  `https://github.com/glockyco/HotRepl`)

**2. Feature cards** (3-column grid)

| Title             | Body                                                                                                                           |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Raw Eval          | Write C# on the game's main thread. Inspect live objects, run one-off repair snippets, explore the runtime interactively.      |
| Typed Commands    | Schema-validated operations registered by the host. Stable contract for repeatable exports, tests, and agent workflows.        |
| Agent & MCP Ready | Connect via WebSocket SDK, CLI, or the nine-tool MCP stdio server. Plug into any coding agent, script, or automation pipeline. |

**3. Quickstart** (Shiki-highlighted TypeScript block, highlighted in `+page.ts` load function)

```typescript
import { connect } from "@hotrepl/sdk";

const session = await connect(); // ws://127.0.0.1:18590 by default
const name = await session.eval("UnityEngine.Application.productName");
const preflight = await session.run("archive.preflight", {});
```

**4. Integration paths** (table)

| Path           | Use for                                | Entry                             |
| -------------- | -------------------------------------- | --------------------------------- |
| Raw eval       | Interactive inspection, one-off repair | `session.eval()` / `hotrepl eval` |
| Typed commands | Automation, exports, repeatable tests  | `session.run()` / `command_call`  |
| CLI            | Shell scripts, local workflows         | `@hotrepl/cli`                    |
| MCP            | AI agent tool catalog                  | `@hotrepl/mcp`                    |
| Host embedding | New loader adapters, test hosts        | `IReplHost` + `ReplEngine.Tick()` |

**5. Real consumers** (two linked cards)

- Ardenfall Compendium → `https://github.com/glockyco/ardenfall-compendium`
- Ancient Kingdoms Compendium → `https://github.com/glockyco/ancient-kingdoms-mods`

**Footer:** GitHub link + "By Johann Glock"

### Protocol reference (`src/routes/protocol/+page.svelte`)

**Layout:** Sticky left nav sidebar (240px wide) + scrollable main content. Below 768px, sidebar
collapses to an inline `<details>` element at the top of the page.

**Message families** (7 groups):

| ID               | Name           | Messages                                                                                                                                                     |
| ---------------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `connection`     | Connection     | `handshake` (S→C), `session_evicted` (S→C)                                                                                                                   |
| `eval`           | Eval           | `eval` (C→S), `eval_result` (S→C), `eval_error` (S→C), `complete` (C→S), `complete_result` (S→C), `reset` (C→S), `reset_result` (S→C)                        |
| `subscriptions`  | Subscriptions  | `subscribe` (C→S), `subscribe_result` (S→C), `subscribe_error` (S→C), `cancel` (C→S)                                                                         |
| `typed-commands` | Typed Commands | `commands_list` (C→S), `commands_list_result` (S→C), `command_describe` (C→S), `command_describe_result` (S→C), `command_call` (C→S), `command_result` (S→C) |
| `jobs`           | Jobs           | `job_accepted` (S→C), `job_status` (C→S), `job_status_result` (S→C), `job_result` (S→C), `job_cancel` (C→S), `job_cancel_result` (S→C)                       |
| `journal`        | Journal        | `journal_query` (C→S), `journal_query_result` (S→C)                                                                                                          |
| `shared-types`   | Shared Types   | `ErrorEnvelope`, `ArtifactRef`, `CommandDescriptor`, `JournalEntry`                                                                                          |

### Data model (`src/lib/data/protocol.ts`)

```typescript
import {
  ArtifactRefSchema,
  CommandDescriptorSchema,
  // ... all schemas
  ErrorEnvelopeSchema,
  EvalErrorMessageSchema,
  EvalMessageSchema,
  EvalResultMessageSchema,
  HandshakeMessageSchema,
  JournalEntrySchema,
  MESSAGE_TYPES,
  SessionEvictedMessageSchema,
} from "@hotrepl/protocol";
import type { TSchema } from "typebox"; // or equivalent 1.x import path
import Value from "typebox/value";

export type Direction = "C→S" | "S→C";

export interface MessageDef {
  type: string; // wire discriminant
  direction: Direction;
  description: string; // one sentence; authored here
  example: string; // raw JSON string; sourced from control-plane-protocol.md
  schema: TSchema; // TypeBox schema; used for build-time example validation
}

export interface SharedTypeDef {
  name: string;
  description: string;
  example: string;
  schema: TSchema;
}

export interface MessageFamily {
  id: string; // anchor slug
  name: string;
  description: string;
  messages: MessageDef[];
}

// Called once in +page.ts load() at prerender time.
// Throws (and fails the build) if any example fails schema validation.
export function validateAllExamples(
  families: MessageFamily[],
  sharedTypes: SharedTypeDef[],
): void {
  for (const family of families) {
    for (const msg of family.messages) {
      const parsed = JSON.parse(msg.example);
      if (!Value.Check(msg.schema, parsed)) {
        const errors = Value.Errors(msg.schema, parsed); // Array in TypeBox 1.x
        throw new Error(
          `Example validation failed for '${msg.type}':\n${JSON.stringify(errors, null, 2)}`,
        );
      }
    }
  }
  for (const t of sharedTypes) {
    const parsed = JSON.parse(t.example);
    if (!Value.Check(t.schema, parsed)) {
      throw new Error(`Example validation failed for shared type '${t.name}'`);
    }
  }
}

// ── Exhaustiveness check ──────────────────────────────────────────────────────
// Ensures every MESSAGE_TYPES value is documented in families[].messages[].
// Produces a TypeScript compile error if any discriminant is absent.
type _AllTypes = (typeof MESSAGE_TYPES)[keyof typeof MESSAGE_TYPES];
type _DocumentedTypes = (typeof families)[number]["messages"][number]["type"];
// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _Exhaustive = [Exclude<_AllTypes, _DocumentedTypes>] extends [never] ? true : never;

// ── Data ─────────────────────────────────────────────────────────────────────
export const families: MessageFamily[] = [
  {
    id: "connection",
    name: "Connection",
    description: "Messages exchanged when a WebSocket connection opens.",
    messages: [
      {
        type: MESSAGE_TYPES.handshake,
        direction: "S→C",
        description:
          "Sent by the server immediately after the WebSocket opens. Advertises host identity, evaluator capabilities, runtime limits, and typed-command support.",
        schema: HandshakeMessageSchema,
        example: `{
  "type": "handshake",
  "protocolVersion": 2,
  "host": { "name": "BepInEx", "version": "0.x", "platform": "Unity Mono" },
  "evaluator": {
    "name": "Mono.CSharp", "languageVersion": "7.x",
    "persistentState": true, "supportsCompletion": true, "cancellation": "hardAbort"
  },
  "availableEvaluators": ["Mono.CSharp"],
  "defaultUsings": ["System"],
  "helpers": ["String[] Help()"],
  "control": { "supported": true, "commandsListChanged": false, "schemaValidation": false },
  "limits": {
    "maxMessageBytes": 4194304, "maxQueuedCommands": 32, "maxResultLength": 102400,
    "maxEnumerableElements": 100, "defaultEvalTimeoutMs": 10000, "maxJobConcurrency": 1
  },
  "enforces": ["maxMessageBytes", "maxQueuedCommands", "maxResultLength",
               "maxEnumerableElements", "maxJobConcurrency"]
}`,
      },
      // session_evicted, and all other families follow the same shape.
    ],
  },
  // ... remaining 6 families
];

export const sharedTypes: SharedTypeDef[] = [
  {
    name: "ErrorEnvelope",
    description: "Unified error representation used in every failure response.",
    schema: ErrorEnvelopeSchema,
    example: `{
  "kind": "validation_failed",
  "code": "badArgument",
  "message": "The command argument is invalid.",
  "retryable": false,
  "details": { "path": "/scene" }
}`,
  },
  // ArtifactRef, CommandDescriptor, JournalEntry follow
];
```

> **`cancel` in the exhaustiveness check:** `MESSAGE_TYPES.cancel` exists and must appear in
> `families["subscriptions"].messages`. The `cancel` message schema is documented even though the
> SDK doesn't currently send it. The check passes as long as the entry exists; the "not yet used by
> SDK" note lives in the description field.

> **`assembly_reload` in the exhaustiveness check:** `MESSAGE_TYPES.assemblyReload` is defined but
> not in the current `ServerMessage` union. Either add a schema for it (with a note in the
> description) or remove it from `MESSAGE_TYPES`. Decide during implementation; do not skip past
> this with a type cast.

### `+page.ts` for `/protocol/`

```typescript
import { families, sharedTypes, validateAllExamples } from "$lib/data/protocol";
import { codeToHtml } from "shiki";
import type { PageLoad } from "./$types";

export const prerender = true;

export const load: PageLoad = async () => {
  // Fail the build if any example is structurally invalid.
  validateAllExamples(families, sharedTypes);

  // Highlight all examples once at prerender time.
  const highlight = (code: string, lang = "json") =>
    codeToHtml(code, { lang, theme: "github-dark" });

  const highlightedFamilies = await Promise.all(
    families.map(async (family) => ({
      ...family,
      messages: await Promise.all(
        family.messages.map(async (msg) => ({
          ...msg,
          exampleHtml: await highlight(msg.example),
          // JSON Schema: JSON.stringify is sufficient in TypeBox 1.x (non-enumerable internals)
          schemaHtml: await highlight(JSON.stringify(msg.schema, null, 2)),
        })),
      ),
    })),
  );

  const highlightedSharedTypes = await Promise.all(
    sharedTypes.map(async (t) => ({
      ...t,
      exampleHtml: await highlight(t.example),
      schemaHtml: await highlight(JSON.stringify(t.schema, null, 2)),
    })),
  );

  return { families: highlightedFamilies, sharedTypes: highlightedSharedTypes };
};
```

### `MessageCard.svelte`

Props:

```typescript
interface Props {
  type: string;
  direction: "C→S" | "S→C";
  description: string;
  exampleHtml: string; // pre-highlighted by Shiki
  schemaHtml: string; // pre-highlighted JSON Schema
}
```

Rendered structure (each card is an `<article>` with an `id={type}` anchor):

1. **Header row:** `<code>{type}</code>` pill in accent color, direction badge (orange for C→S, blue
   for S→C), `[v2]` tag in muted text.
2. **Description** paragraph.
3. **Example** block: label "Example" + `{@html exampleHtml}` (Shiki output).
4. **JSON Schema** (collapsible `<details>`): label "JSON Schema" + `{@html schemaHtml}`.

### `ProtocolNav.svelte`

Props:

```typescript
interface Props {
  families: Array<{ id: string; name: string; messages: Array<{ type: string }> }>;
}
```

Renders a `<nav>` containing:

- `← Back` link to `/`
- "Protocol Reference" heading
- One `<section>` per family: family name as label, then one `<a href="#{type}">` per message
- Active message tracking via `IntersectionObserver` (progressive enhancement; the page works
  without JS)

---

## Part 5 — Personal website integration

### `src/lib/data/projects.ts`

Insert between the `personal-website` and `10-man-idle` entries:

```typescript
{
  slug: 'hotrepl',
  title: 'HotRepl',
  tagline: 'Runtime C# REPL and typed command bridge for Unity games',
  status: 'active' as const,
  featured: false,
  inPdfCv: false,
  liveUrl: 'https://hotrepl.glockyco.com/',
  githubUrl: 'https://github.com/glockyco/HotRepl',
  techStack: [
    'C#', '.NET', 'TypeScript', 'Bun', 'Unity',
    'BepInEx', 'MelonLoader', 'WebSocket',
    'SvelteKit', 'Cloudflare Workers',
  ],
},
```

### Screenshot workflow

Once the site is deployed and `hotrepl.glockyco.com` is live:

1. Run `pnpm screenshots` from `~/Projects/personal-website`. The script already handles
   `liveUrl`-based projects; no script changes needed. The `?theme=dark` param appended by the
   script is silently ignored by the dark-only site.
2. Add `hotrepl-thumb.webp` and `hotrepl-hero.webp` imports and entries to
   `src/lib/assets/screenshots/index.ts`.

No changes to the screenshot script. No GitHub-based screenshot handling needed.

---

## Out of scope

- Theme toggle / light mode for the HotRepl site
- Live WebSocket console / "try it" panel
- Full-text search
- Versioning (site documents v2 only)
- AsyncAPI spec generation
- `cancel` message implementation in the SDK (schema exists; SDK behavior unchanged)
- Upgrading any package other than `packages/protocol` to TypeBox 1.x (other packages use only type
  imports from the protocol; no direct TypeBox dependency)
- Changes to `packages/conformance`, `packages/sdk`, `packages/cli`, `packages/mcp`,
  `packages/testing` beyond any mechanical import-path fixes forced by the protocol package's type
  re-exports changing shape (expected: none, since type aliases remain compatible)
