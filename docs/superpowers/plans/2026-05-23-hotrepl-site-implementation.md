# HotRepl Site Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `hotrepl.glockyco.com` — a SvelteKit documentation site with a landing page and
auto-validated protocol reference — backed by a complete TypeBox 1.x migration of
`packages/protocol`.

**Architecture:** Upgrade `@sinclair/typebox` 0.34 → `typebox` 1.x, migrate all protocol message
types to TypeBox schemas (enabling build-time JSON example validation), scaffold a `site/` SvelteKit
app in the HotRepl Bun workspace, prerender both routes with Shiki highlighting in server-only load
functions, deploy to `hotrepl.glockyco.com` via Cloudflare Workers, then add the project to
`personal-website/src/lib/data/projects.ts`.

**Tech Stack:** TypeBox 1.x, SvelteKit 2 + Svelte 5, Tailwind CSS v4 (`@tailwindcss/vite`), Shiki
v3, `@sveltejs/adapter-cloudflare`, Wrangler 4, Bun workspaces, Cloudflare Workers.

**Spec corrections applied in this plan (vs. spec file):**

- `+page.server.ts` replaces `+page.ts` for Shiki routes (keeps Shiki out of browser bundle)
- `Type.Strict()` removed everywhere — does not exist in typebox 1.x; `JSON.stringify(schema)`
  already produces clean JSON Schema
- `optimizeDeps.exclude` removed from `vite.config.ts` — Vite handles linked ESM workspace deps
  automatically
- `command_call.args` is `JsonObjectSchema`, not `Type.Unknown()` (C# runtime expects JObject;
  `Type.Unknown()` would accept invalid values like integers or arrays)
- `assembly_reload` added as a real server-sent message:
  `{ type, assembly?: string, message: string }`
- Exhaustiveness check uses a runtime Set assertion, not a broken type trick
- `cancel` example written fresh (not in `control-plane-protocol.md`)

---

## File Map

### `~/Projects/HotRepl` (primary repo)

**Modified:**

- `package.json` — add `site` to workspaces
- `packages/protocol/package.json` — upgrade typebox
- `packages/protocol/src/handshake.ts` — update import
- `packages/protocol/src/messages.ts` — full rewrite: shared types + server + client schemas
- `packages/protocol/scripts/export-schemas.ts` — rewrite to export all schemas
- `packages/protocol/test/handshake.test.ts` — update import
- `docs/control-plane-protocol.md` — add assembly_reload + cancel entries

**Created:**

- `packages/protocol/test/messages.test.ts` — Value.Check tests for every schema
- `packages/protocol/schemas/*.schema.json` — generated (run by export script)
- `site/` — entire new SvelteKit application (see breakdown below)

### `site/` file breakdown

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
      +layout.svelte
      +page.server.ts        ← landing page server load (Shiki for quickstart)
      +page.svelte           ← landing page
      protocol/
        +page.server.ts      ← validates examples + runs Shiki (server-only)
        +page.svelte         ← protocol reference page
    lib/
      data/
        protocol.ts          ← all families, examples, schemas, runtime assertions
      components/
        MessageCard.svelte
        ProtocolNav.svelte
```

### `~/Projects/personal-website` (secondary repo)

**Modified:**

- `src/lib/data/projects.ts` — add hotrepl entry between personal-website and 10-man-idle
- `src/lib/assets/screenshots/index.ts` — add hotrepl thumb + hero imports

**Generated:**

- `src/lib/assets/screenshots/hotrepl-thumb.webp`
- `src/lib/assets/screenshots/hotrepl-hero.webp`

---

## Task 1: TypeBox upgrade — `@sinclair/typebox` 0.34 → `typebox` 1.x

**Files:**

- Modify: `packages/protocol/package.json`
- Modify: `packages/protocol/src/handshake.ts`
- Modify: `packages/protocol/test/handshake.test.ts`

- [ ] **Step 1: Install typebox 1.x, remove old package**

Run from `~/Projects/HotRepl`:

```bash
cd packages/protocol
bun remove @sinclair/typebox
bun add typebox@^1.0.0
cd ../..
bun install
```

Expected: `bun.lock` updated, `typebox` appears in `packages/protocol/package.json` dependencies.

- [ ] **Step 2: Update `packages/protocol/src/handshake.ts` import**

Change the first line only:

```typescript
// Before:
import { type Static, Type } from "@sinclair/typebox";

// After:
import { type Static, Type } from "typebox";
```

The rest of `handshake.ts` is unchanged — all `Type.*` constructors are identical in 1.x.

- [ ] **Step 3: Update `packages/protocol/test/handshake.test.ts` import**

Change the Value import:

```typescript
// Before:
import { Value } from "@sinclair/typebox/value";

// After:
import { Value } from "typebox/value";
```

`Value.Check(schema, value)` signature is unchanged. `Value.Errors(schema, value)` now returns
`TLocalizedValidationError[]` (array, not IterableIterator) — no spread needed.

- [ ] **Step 4: Run existing tests to confirm the upgrade is clean**

```bash
cd ~/Projects/HotRepl
bun test packages/protocol/test/handshake.test.ts
```

Expected output:

```
bun test v1.x.x
packages/protocol/test/handshake.test.ts:
✓ protocol foundations > exports the locked v2 constants
✓ protocol foundations > validates an honest handshake with enforced limits

2 pass, 0 fail
```

- [ ] **Step 5: Run typecheck**

```bash
cd ~/Projects/HotRepl/packages/protocol
bun run typecheck
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
cd ~/Projects/HotRepl
git add packages/protocol/package.json packages/protocol/src/handshake.ts packages/protocol/test/handshake.test.ts bun.lock
git commit -m "build(protocol): upgrade @sinclair/typebox 0.34 to typebox 1.x

Package renamed from @sinclair/typebox to typebox (unscoped). ESM-only.
No breaking changes affect HotRepl usage: Value.Check API, Type.*
constructors, and Static<T> type export are all compatible.
JSON.stringify(schema) now produces clean JSON Schema without Type.Strict()
since internal properties (~kind etc.) are non-enumerable in 1.x."
```

---

## Task 2: Shared type schemas

**Files:**

- Modify: `packages/protocol/src/messages.ts`

Convert `HotReplErrorEnvelope`, `ArtifactRef`, `CommandSummary`, `CommandDescriptor`, `JournalEntry`
from plain interfaces to TypeBox schemas. Add new `JsonObjectSchema`. This is the foundation that
server and client message schemas depend on.

- [ ] **Step 1: Replace the top of `messages.ts` with shared type schemas**

Replace the entire `messages.ts` file content up to (but not including) the existing
`EvalResultMessage` interface. The new content:

```typescript
import { type Static, Type } from "typebox";
import { Value } from "typebox/value";
import type { ErrorKind } from "./error-kinds";
import { ERROR_KINDS } from "./error-kinds";
import type { HandshakeMessage } from "./handshake";
import { MESSAGE_TYPES } from "./message-types";

export type JsonObject = Record<string, unknown>;

// ── Shared types ──────────────────────────────────────────────────────────────

/** Opaque JSON object — used for command args and inline JSON schemas */
export const JsonObjectSchema = Type.Record(Type.String(), Type.Unknown());

/** Unified error envelope used in every failure response */
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

/** Named reference to a file artifact produced by a command */
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

/** Summary of a registered typed command */
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

/** Full descriptor for a registered typed command, including I/O schemas.
 *  Fields duplicated from CommandSummary deliberately (avoids anyOf/allOf in
 *  the exported JSON Schema, which makes the docs harder to read). */
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

/** One eval or command entry in the journal */
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

- [ ] **Step 2: Verify typecheck still passes**

```bash
cd ~/Projects/HotRepl/packages/protocol
bun run typecheck
```

Expected: no errors (the existing server message interfaces below still compile fine against the new
shared types because they only reference the TypeScript types, not the schemas).

- [ ] **Step 3: Commit**

```bash
cd ~/Projects/HotRepl
git add packages/protocol/src/messages.ts
git commit -m "feat(protocol): add TypeBox schemas for shared types

Converts HotReplErrorEnvelope, ArtifactRef, CommandSummary,
CommandDescriptor, JournalEntry from plain interfaces to TypeBox schemas.
Adds JsonObjectSchema for opaque JSON objects (command args, inline schemas).

ERROR_KINDS union is constructed from the existing const array so both the
TypeScript union and the TypeBox schema stay in sync automatically.
CommandDescriptor intentionally inlines CommandSummary fields to keep
exported JSON Schema flat and readable."
```

---

## Task 3: Server message schemas

**Files:**

- Modify: `packages/protocol/src/messages.ts`

Replace all existing `export interface XxxMessage` declarations with TypeBox schema + type alias
pairs. Add `AssemblyReloadMessage` (new). Update the `ServerMessage` union.

- [ ] **Step 1: Replace all server message interfaces in `messages.ts`**

After the shared-type block from Task 2, add all server message schemas. Replace every existing
`export interface XxxMessage { ... }` and the `ServerMessage` union with:

```typescript
// ── Server-sent messages ──────────────────────────────────────────────────────

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
  {
    type: Type.Literal(MESSAGE_TYPES.evalError),
    id: Type.String(),
    error: ErrorEnvelopeSchema,
  },
  { additionalProperties: false },
);
export type EvalErrorMessage = Static<typeof EvalErrorMessageSchema>;

export const CompleteResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.completeResult),
    id: Type.String(),
    completions: Type.Array(Type.String()),
    durationMs: Type.Number(),
  },
  { additionalProperties: false },
);
export type CompleteResultMessage = Static<typeof CompleteResultMessageSchema>;

export const ResetResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.resetResult),
    id: Type.String(),
    success: Type.Boolean(),
  },
  { additionalProperties: false },
);
export type ResetResultMessage = Static<typeof ResetResultMessageSchema>;

export const SubscribeResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.subscribeResult),
    id: Type.String(),
    seq: Type.Number(),
    hasValue: Type.Boolean(),
    value: Type.Optional(Type.Unknown()),
    valueType: Type.Optional(Type.String()),
    durationMs: Type.Number(),
    final: Type.Boolean(),
  },
  { additionalProperties: false },
);
export type SubscribeResultMessage = Static<typeof SubscribeResultMessageSchema>;

export const SubscribeErrorMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.subscribeError),
    id: Type.String(),
    seq: Type.Number(),
    error: ErrorEnvelopeSchema,
    final: Type.Boolean(),
  },
  { additionalProperties: false },
);
export type SubscribeErrorMessage = Static<typeof SubscribeErrorMessageSchema>;

export const SessionEvictedMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.sessionEvicted),
    reason: Type.String(),
    by: Type.Optional(
      Type.Object({ clientName: Type.Optional(Type.String()) }, { additionalProperties: false }),
    ),
  },
  { additionalProperties: false },
);
export type SessionEvictedMessage = Static<typeof SessionEvictedMessageSchema>;

export const ProtocolErrorMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.error),
    id: Type.Optional(Type.String()),
    error: ErrorEnvelopeSchema,
  },
  { additionalProperties: false },
);
export type ProtocolErrorMessage = Static<typeof ProtocolErrorMessageSchema>;

export const CommandsListResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.commandsListResult),
    id: Type.String(),
    commands: Type.Array(CommandSummarySchema),
    since: Type.Optional(Type.String()),
  },
  { additionalProperties: false },
);
export type CommandsListResultMessage = Static<typeof CommandsListResultMessageSchema>;

export const CommandDescribeResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.commandDescribeResult),
    id: Type.String(),
    descriptor: CommandDescriptorSchema,
  },
  { additionalProperties: false },
);
export type CommandDescribeResultMessage = Static<typeof CommandDescribeResultMessageSchema>;

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

export const JobAcceptedMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.jobAccepted),
    id: Type.String(),
    jobId: Type.String(),
    state: Type.Literal("running"),
  },
  { additionalProperties: false },
);
export type JobAcceptedMessage = Static<typeof JobAcceptedMessageSchema>;

export const JobStatusResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.jobStatusResult),
    id: Type.String(),
    jobId: Type.String(),
    state: Type.Literal("running"),
    progress: Type.Optional(Type.Unknown()),
    error: Type.Optional(ErrorEnvelopeSchema),
  },
  { additionalProperties: false },
);
export type JobStatusResultMessage = Static<typeof JobStatusResultMessageSchema>;

export const JobResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.jobResult),
    id: Type.String(),
    jobId: Type.String(),
    state: Type.Union([
      Type.Literal("done"),
      Type.Literal("failed"),
      Type.Literal("cancelled"),
    ]),
    status: Type.Union([Type.Literal("ok"), Type.Literal("failed")]),
    output: Type.Optional(Type.Unknown()),
    artifacts: Type.Record(Type.String(), ArtifactRefSchema),
    error: Type.Optional(ErrorEnvelopeSchema),
    durationMs: Type.Number(),
  },
  { additionalProperties: false },
);
export type JobResultMessage = Static<typeof JobResultMessageSchema>;

export const JobCancelResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.jobCancelResult),
    id: Type.String(),
    accepted: Type.Boolean(),
    state: Type.Union([
      Type.Literal("running"),
      Type.Literal("done"),
      Type.Literal("failed"),
      Type.Literal("cancelled"),
    ]),
  },
  { additionalProperties: false },
);
export type JobCancelResultMessage = Static<typeof JobCancelResultMessageSchema>;

export const JournalQueryResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.journalQueryResult),
    id: Type.String(),
    entries: Type.Array(JournalEntrySchema),
  },
  { additionalProperties: false },
);
export type JournalQueryResultMessage = Static<typeof JournalQueryResultMessageSchema>;

/** Sent by the server when a game assembly is hot-reloaded.
 *  Currently unhandled by the SDK transport (dropped silently). */
export const AssemblyReloadMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.assemblyReload),
    assembly: Type.Optional(Type.String()),
    message: Type.String(),
  },
  { additionalProperties: false },
);
export type AssemblyReloadMessage = Static<typeof AssemblyReloadMessageSchema>;

// ── ServerMessage union ───────────────────────────────────────────────────────

export const ServerMessageSchema = Type.Union([
  // handshake is defined in handshake.ts; import it at the top of this file
  // via the existing `import type { HandshakeMessage } from "./handshake"` —
  // add a value import too:
  //   import { HandshakeMessageSchema } from "./handshake";
  // (do this in the same edit pass)
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
  AssemblyReloadMessageSchema,
]);

export type ServerMessage =
  | HandshakeMessage
  | EvalResultMessage
  | EvalErrorMessage
  | CompleteResultMessage
  | ResetResultMessage
  | SubscribeResultMessage
  | SubscribeErrorMessage
  | SessionEvictedMessage
  | ProtocolErrorMessage
  | CommandsListResultMessage
  | CommandDescribeResultMessage
  | CommandResultMessage
  | JobAcceptedMessage
  | JobStatusResultMessage
  | JobResultMessage
  | JobCancelResultMessage
  | JournalQueryResultMessage
  | AssemblyReloadMessage;
```

> **Note on HandshakeMessageSchema in ServerMessageSchema:** also add a value import of
> `HandshakeMessageSchema` from `./handshake` and include it first in the
> `Type.Union([HandshakeMessageSchema, EvalResultMessageSchema, ...])` array.

- [ ] **Step 2: Update the import in `messages.ts` to include `HandshakeMessageSchema`**

Add to the existing handshake import line:

```typescript
import { type HandshakeMessage, HandshakeMessageSchema } from "./handshake";
```

Then add `HandshakeMessageSchema` as the first element of the `Type.Union([ ... ])` call in
`ServerMessageSchema`.

- [ ] **Step 3: Typecheck**

```bash
cd ~/Projects/HotRepl/packages/protocol
bun run typecheck
```

Expected: no errors. All SDK imports of types like `EvalResultMessage`, `CommandResultMessage`, etc.
continue to work since the type aliases have the same shape as the original interfaces.

- [ ] **Step 4: Run existing tests**

```bash
cd ~/Projects/HotRepl
bun test packages/protocol/test/handshake.test.ts
```

Expected: 2 pass.

- [ ] **Step 5: Commit**

```bash
cd ~/Projects/HotRepl
git add packages/protocol/src/messages.ts
git commit -m "feat(protocol): migrate all server message interfaces to TypeBox schemas

Converts all 16 existing server message interfaces plus adds
AssemblyReloadMessage (new — matches C# ReplEngine.HandleHotReload output).
All type aliases remain backward-compatible; callers outside packages/protocol
require no changes since Static<typeof XxxSchema> == original interface.

ServerMessageSchema union now covers all 18 server-sent message types."
```

---

## Task 4: Client message schemas

**Files:**

- Modify: `packages/protocol/src/messages.ts`

Add TypeBox schemas for all client-sent messages. Source of truth for shapes is `RuntimeRequest` in
`packages/sdk/src/session.ts`. `cancel` is defined by the C# `CancelMessage` record (not yet in
`RuntimeRequest`).

- [ ] **Step 1: Add client message schemas to `messages.ts` after the ServerMessage union**

```typescript
// ── Client-sent messages ──────────────────────────────────────────────────────

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

/** Defined in C# CancelMessage; not yet used by the SDK RuntimeRequest.
 *  Cancels an active eval or subscription by its request id. */
export const CancelMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.cancel),
    id: Type.String(),
    targetId: Type.String(),
  },
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
  {
    type: Type.Literal(MESSAGE_TYPES.commandDescribe),
    id: Type.String(),
    name: Type.String(),
  },
  { additionalProperties: false },
);
export type CommandDescribeMessage = Static<typeof CommandDescribeMessageSchema>;

/** args must be a JSON object — the C# runtime deserializes into JObject. */
export const CommandCallMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.commandCall),
    id: Type.String(),
    name: Type.String(),
    args: JsonObjectSchema,
    timeoutMs: Type.Optional(Type.Number()),
  },
  { additionalProperties: false },
);
export type CommandCallMessage = Static<typeof CommandCallMessageSchema>;

export const JobStatusMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.jobStatus),
    id: Type.String(),
    jobId: Type.String(),
  },
  { additionalProperties: false },
);
export type JobStatusMessage = Static<typeof JobStatusMessageSchema>;

export const JobCancelMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.jobCancel),
    id: Type.String(),
    jobId: Type.String(),
  },
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

- [ ] **Step 2: Typecheck**

```bash
cd ~/Projects/HotRepl/packages/protocol
bun run typecheck
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
cd ~/Projects/HotRepl
git add packages/protocol/src/messages.ts
git commit -m "feat(protocol): add TypeBox schemas for all client-sent messages

Covers eval, complete, reset, subscribe, cancel, commands_list,
command_describe, command_call, job_status, job_cancel, journal_query.
Source of truth for shapes: RuntimeRequest in packages/sdk/src/session.ts
plus C# CancelMessage for the cancel type (not yet in RuntimeRequest).

command_call.args is JsonObjectSchema (permissive object) not Type.Unknown():
the C# runtime deserializes args into JObject and rejects non-object values."
```

---

## Task 5: Protocol schema tests

**Files:**

- Create: `packages/protocol/test/messages.test.ts`

One `Value.Check(schema, minimal_valid_example)` assertion per schema. Tests confirm schemas accept
valid examples and are importable. Tests do NOT verify rejection of invalid values (that would be
unit-testing TypeBox itself).

- [ ] **Step 1: Create `packages/protocol/test/messages.test.ts`**

```typescript
import { describe, expect, test } from "bun:test";
import { Value } from "typebox/value";
import {
  ArtifactRefSchema,
  AssemblyReloadMessageSchema,
  CancelMessageSchema,
  CommandCallMessageSchema,
  CommandDescribeMessageSchema,
  CommandDescribeResultMessageSchema,
  CommandDescriptorSchema,
  CommandResultMessageSchema,
  CommandsListMessageSchema,
  CommandsListResultMessageSchema,
  CommandSummarySchema,
  CompleteMessageSchema,
  CompleteResultMessageSchema,
  // Shared types
  ErrorEnvelopeSchema,
  EvalErrorMessageSchema,
  // Client messages
  EvalMessageSchema,
  // Server messages
  EvalResultMessageSchema,
  JobAcceptedMessageSchema,
  JobCancelMessageSchema,
  JobCancelResultMessageSchema,
  JobResultMessageSchema,
  JobStatusMessageSchema,
  JobStatusResultMessageSchema,
  JournalEntrySchema,
  JournalQueryMessageSchema,
  JournalQueryResultMessageSchema,
  JsonObjectSchema,
  MESSAGE_TYPES,
  ProtocolErrorMessageSchema,
  ResetMessageSchema,
  ResetResultMessageSchema,
  SessionEvictedMessageSchema,
  SubscribeErrorMessageSchema,
  SubscribeMessageSchema,
  SubscribeResultMessageSchema,
} from "../src";

const ERR = {
  kind: "internal" as const,
  code: "runtimeException",
  message: "Something went wrong.",
  retryable: false,
};

const ARTIFACT = {
  uri: "file:///exports/items.json",
  sha256: "abc123",
  byteSize: 100,
  contentType: "application/json",
  finalized: true,
};

describe("shared type schemas", () => {
  test("JsonObjectSchema validates empty object", () => {
    expect(Value.Check(JsonObjectSchema, {})).toBe(true);
  });

  test("ErrorEnvelopeSchema validates minimal error", () => {
    expect(Value.Check(ErrorEnvelopeSchema, ERR)).toBe(true);
  });

  test("ErrorEnvelopeSchema validates with details", () => {
    expect(Value.Check(ErrorEnvelopeSchema, { ...ERR, details: { path: "/x" } })).toBe(true);
  });

  test("ArtifactRefSchema validates minimal artifact", () => {
    expect(Value.Check(ArtifactRefSchema, ARTIFACT)).toBe(true);
  });

  test("CommandSummarySchema validates sync command", () => {
    expect(
      Value.Check(CommandSummarySchema, {
        name: "archive.preflight",
        majorVersion: 1,
        kind: "sync",
        mutatesState: false,
      }),
    ).toBe(true);
  });

  test("CommandDescriptorSchema validates with schemas", () => {
    expect(
      Value.Check(CommandDescriptorSchema, {
        name: "archive.preflight",
        majorVersion: 1,
        kind: "sync",
        mutatesState: false,
        inputSchema: {},
        outputSchema: {},
        artifactsSchema: {},
      }),
    ).toBe(true);
  });

  test("JournalEntrySchema validates eval entry", () => {
    expect(
      Value.Check(JournalEntrySchema, {
        id: "eval-1",
        kind: "eval",
        code: "1 + 1",
        success: true,
        durationMs: 3,
        timestamp: "2026-05-23T12:00:00.000Z",
      }),
    ).toBe(true);
  });
});

describe("server message schemas", () => {
  test("eval_result (no value)", () => {
    expect(
      Value.Check(EvalResultMessageSchema, {
        type: MESSAGE_TYPES.evalResult,
        id: "e1",
        hasValue: false,
        durationMs: 3,
      }),
    ).toBe(true);
  });

  test("eval_result (with value)", () => {
    expect(
      Value.Check(EvalResultMessageSchema, {
        type: MESSAGE_TYPES.evalResult,
        id: "e1",
        hasValue: true,
        value: "42",
        valueType: "System.Int32",
        durationMs: 3,
      }),
    ).toBe(true);
  });

  test("eval_error", () => {
    expect(
      Value.Check(EvalErrorMessageSchema, { type: MESSAGE_TYPES.evalError, id: "e1", error: ERR }),
    ).toBe(true);
  });

  test("complete_result", () => {
    expect(
      Value.Check(CompleteResultMessageSchema, {
        type: MESSAGE_TYPES.completeResult,
        id: "c1",
        completions: ["productName"],
        durationMs: 5,
      }),
    ).toBe(true);
  });

  test("reset_result", () => {
    expect(
      Value.Check(ResetResultMessageSchema, {
        type: MESSAGE_TYPES.resetResult,
        id: "r1",
        success: true,
      }),
    ).toBe(true);
  });

  test("subscribe_result (not final)", () => {
    expect(
      Value.Check(SubscribeResultMessageSchema, {
        type: MESSAGE_TYPES.subscribeResult,
        id: "w1",
        seq: 0,
        hasValue: true,
        value: "42",
        durationMs: 3,
        final: false,
      }),
    ).toBe(true);
  });

  test("subscribe_error", () => {
    expect(
      Value.Check(SubscribeErrorMessageSchema, {
        type: MESSAGE_TYPES.subscribeError,
        id: "w1",
        seq: 0,
        error: ERR,
        final: true,
      }),
    ).toBe(true);
  });

  test("session_evicted", () => {
    expect(
      Value.Check(SessionEvictedMessageSchema, {
        type: MESSAGE_TYPES.sessionEvicted,
        reason: "new_connection",
      }),
    ).toBe(true);
  });

  test("error (protocol error, no id)", () => {
    expect(
      Value.Check(ProtocolErrorMessageSchema, { type: MESSAGE_TYPES.error, error: ERR }),
    ).toBe(true);
  });

  test("commands_list_result", () => {
    expect(
      Value.Check(CommandsListResultMessageSchema, {
        type: MESSAGE_TYPES.commandsListResult,
        id: "l1",
        commands: [{
          name: "archive.preflight",
          majorVersion: 1,
          kind: "sync",
          mutatesState: false,
        }],
      }),
    ).toBe(true);
  });

  test("command_describe_result", () => {
    expect(
      Value.Check(CommandDescribeResultMessageSchema, {
        type: MESSAGE_TYPES.commandDescribeResult,
        id: "d1",
        descriptor: {
          name: "archive.preflight",
          majorVersion: 1,
          kind: "sync",
          mutatesState: false,
          inputSchema: {},
          outputSchema: {},
          artifactsSchema: {},
        },
      }),
    ).toBe(true);
  });

  test("command_result (ok, sync)", () => {
    expect(
      Value.Check(CommandResultMessageSchema, {
        type: MESSAGE_TYPES.commandResult,
        id: "cmd1",
        status: "ok",
        output: { ok: true },
        artifacts: {},
        durationMs: 12,
      }),
    ).toBe(true);
  });

  test("command_result (failed)", () => {
    expect(
      Value.Check(CommandResultMessageSchema, {
        type: MESSAGE_TYPES.commandResult,
        id: "cmd1",
        status: "failed",
        artifacts: {},
        error: ERR,
        durationMs: 5,
      }),
    ).toBe(true);
  });

  test("job_accepted", () => {
    expect(
      Value.Check(JobAcceptedMessageSchema, {
        type: MESSAGE_TYPES.jobAccepted,
        id: "cmd1",
        jobId: "job-1",
        state: "running",
      }),
    ).toBe(true);
  });

  test("job_status_result", () => {
    expect(
      Value.Check(JobStatusResultMessageSchema, {
        type: MESSAGE_TYPES.jobStatusResult,
        id: "s1",
        jobId: "job-1",
        state: "running",
      }),
    ).toBe(true);
  });

  test("job_result (done)", () => {
    expect(
      Value.Check(JobResultMessageSchema, {
        type: MESSAGE_TYPES.jobResult,
        id: "s1",
        jobId: "job-1",
        state: "done",
        status: "ok",
        output: { ok: true },
        artifacts: {},
        durationMs: 1500,
      }),
    ).toBe(true);
  });

  test("job_cancel_result", () => {
    expect(
      Value.Check(JobCancelResultMessageSchema, {
        type: MESSAGE_TYPES.jobCancelResult,
        id: "jc1",
        accepted: true,
        state: "running",
      }),
    ).toBe(true);
  });

  test("journal_query_result", () => {
    expect(
      Value.Check(JournalQueryResultMessageSchema, {
        type: MESSAGE_TYPES.journalQueryResult,
        id: "j1",
        entries: [],
      }),
    ).toBe(true);
  });

  test("assembly_reload (minimal)", () => {
    expect(
      Value.Check(AssemblyReloadMessageSchema, {
        type: MESSAGE_TYPES.assemblyReload,
        message: "Reloading HotRepl.Plugin.dll",
      }),
    ).toBe(true);
  });

  test("assembly_reload (with assembly)", () => {
    expect(
      Value.Check(AssemblyReloadMessageSchema, {
        type: MESSAGE_TYPES.assemblyReload,
        assembly: "HotRepl.Plugin.dll",
        message: "Assembly reload complete.",
      }),
    ).toBe(true);
  });
});

describe("client message schemas", () => {
  test("eval (no timeout)", () => {
    expect(
      Value.Check(EvalMessageSchema, { type: MESSAGE_TYPES.eval, id: "e1", code: "1 + 1" }),
    ).toBe(true);
  });

  test("eval (with timeout)", () => {
    expect(
      Value.Check(EvalMessageSchema, {
        type: MESSAGE_TYPES.eval,
        id: "e1",
        code: "1 + 1",
        timeoutMs: 5000,
      }),
    ).toBe(true);
  });

  test("complete", () => {
    expect(
      Value.Check(CompleteMessageSchema, {
        type: MESSAGE_TYPES.complete,
        id: "c1",
        code: "UnityEngine.",
      }),
    ).toBe(true);
  });

  test("reset", () => {
    expect(
      Value.Check(ResetMessageSchema, { type: MESSAGE_TYPES.reset, id: "r1" }),
    ).toBe(true);
  });

  test("subscribe", () => {
    expect(
      Value.Check(SubscribeMessageSchema, {
        type: MESSAGE_TYPES.subscribe,
        id: "w1",
        code: "Time.frameCount",
      }),
    ).toBe(true);
  });

  test("cancel", () => {
    expect(
      Value.Check(CancelMessageSchema, {
        type: MESSAGE_TYPES.cancel,
        id: "x1",
        targetId: "w1",
      }),
    ).toBe(true);
  });

  test("commands_list", () => {
    expect(
      Value.Check(CommandsListMessageSchema, { type: MESSAGE_TYPES.commandsList, id: "l1" }),
    ).toBe(true);
  });

  test("command_describe", () => {
    expect(
      Value.Check(CommandDescribeMessageSchema, {
        type: MESSAGE_TYPES.commandDescribe,
        id: "d1",
        name: "archive.preflight",
      }),
    ).toBe(true);
  });

  test("command_call (empty args)", () => {
    expect(
      Value.Check(CommandCallMessageSchema, {
        type: MESSAGE_TYPES.commandCall,
        id: "cmd1",
        name: "archive.preflight",
        args: {},
      }),
    ).toBe(true);
  });

  test("command_call (with args)", () => {
    expect(
      Value.Check(CommandCallMessageSchema, {
        type: MESSAGE_TYPES.commandCall,
        id: "cmd1",
        name: "archive.export",
        args: { scene: "Forest", format: "json" },
      }),
    ).toBe(true);
  });

  test("job_status", () => {
    expect(
      Value.Check(JobStatusMessageSchema, {
        type: MESSAGE_TYPES.jobStatus,
        id: "s1",
        jobId: "job-1",
      }),
    ).toBe(true);
  });

  test("job_cancel", () => {
    expect(
      Value.Check(JobCancelMessageSchema, {
        type: MESSAGE_TYPES.jobCancel,
        id: "jc1",
        jobId: "job-1",
      }),
    ).toBe(true);
  });

  test("journal_query (minimal)", () => {
    expect(
      Value.Check(JournalQueryMessageSchema, { type: MESSAGE_TYPES.journalQuery, id: "jq1" }),
    ).toBe(true);
  });

  test("journal_query (with filter)", () => {
    expect(
      Value.Check(JournalQueryMessageSchema, {
        type: MESSAGE_TYPES.journalQuery,
        id: "jq1",
        kind: "command",
        limit: 20,
      }),
    ).toBe(true);
  });
});
```

- [ ] **Step 2: Run tests**

```bash
cd ~/Projects/HotRepl
bun test packages/protocol/test/
```

Expected: all tests in both `handshake.test.ts` and `messages.test.ts` pass. Count: 2 + ~40 = ~42
passing.

- [ ] **Step 3: Commit**

```bash
cd ~/Projects/HotRepl
git add packages/protocol/test/messages.test.ts
git commit -m "test(protocol): add Value.Check tests for all message schemas

One positive assertion per schema (minimal valid example). Covers all
17 shared types, server messages, and client messages. The handshake
schema continues to be tested in handshake.test.ts."
```

---

## Task 6: Schema export script

**Files:**

- Modify: `packages/protocol/scripts/export-schemas.ts`
- Creates: `packages/protocol/schemas/*.schema.json` (generated artifacts)
- Modify: `docs/control-plane-protocol.md` (add assembly_reload and cancel)

In TypeBox 1.x, `JSON.stringify(schema)` already produces clean JSON Schema — no `Type.Strict()`
needed.

- [ ] **Step 1: Rewrite `packages/protocol/scripts/export-schemas.ts`**

```typescript
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  ArtifactRefSchema,
  AssemblyReloadMessageSchema,
  CancelMessageSchema,
  CommandCallMessageSchema,
  CommandDescribeMessageSchema,
  CommandDescribeResultMessageSchema,
  CommandDescriptorSchema,
  CommandResultMessageSchema,
  CommandsListMessageSchema,
  CommandsListResultMessageSchema,
  CommandSummarySchema,
  CompleteMessageSchema,
  CompleteResultMessageSchema,
  // Shared
  ErrorEnvelopeSchema,
  EvalErrorMessageSchema,
  // Client messages
  EvalMessageSchema,
  // Server messages
  EvalResultMessageSchema,
  JobAcceptedMessageSchema,
  JobCancelMessageSchema,
  JobCancelResultMessageSchema,
  JobResultMessageSchema,
  JobStatusMessageSchema,
  JobStatusResultMessageSchema,
  JournalEntrySchema,
  JournalQueryMessageSchema,
  JournalQueryResultMessageSchema,
  JsonObjectSchema,
  ProtocolErrorMessageSchema,
  ResetMessageSchema,
  ResetResultMessageSchema,
  SessionEvictedMessageSchema,
  SubscribeErrorMessageSchema,
  SubscribeMessageSchema,
  SubscribeResultMessageSchema,
} from "../src";
import { HandshakeMessageSchema } from "../src/handshake";

const schemaDir = join(dirname(fileURLToPath(import.meta.url)), "..", "schemas");
await mkdir(schemaDir, { recursive: true });

// In TypeBox 1.x, internal properties (~kind, ~readonly, ~optional) are
// non-enumerable, so JSON.stringify already produces clean JSON Schema.
const entries: Array<{ file: string; schema: unknown }> = [
  // Handshake (was the only entry before)
  { file: "handshake.schema.json", schema: HandshakeMessageSchema },
  // Server messages
  { file: "eval_result.schema.json", schema: EvalResultMessageSchema },
  { file: "eval_error.schema.json", schema: EvalErrorMessageSchema },
  { file: "complete_result.schema.json", schema: CompleteResultMessageSchema },
  { file: "reset_result.schema.json", schema: ResetResultMessageSchema },
  { file: "subscribe_result.schema.json", schema: SubscribeResultMessageSchema },
  { file: "subscribe_error.schema.json", schema: SubscribeErrorMessageSchema },
  { file: "session_evicted.schema.json", schema: SessionEvictedMessageSchema },
  { file: "error.schema.json", schema: ProtocolErrorMessageSchema },
  { file: "commands_list_result.schema.json", schema: CommandsListResultMessageSchema },
  { file: "command_describe_result.schema.json", schema: CommandDescribeResultMessageSchema },
  { file: "command_result.schema.json", schema: CommandResultMessageSchema },
  { file: "job_accepted.schema.json", schema: JobAcceptedMessageSchema },
  { file: "job_status_result.schema.json", schema: JobStatusResultMessageSchema },
  { file: "job_result.schema.json", schema: JobResultMessageSchema },
  { file: "job_cancel_result.schema.json", schema: JobCancelResultMessageSchema },
  { file: "journal_query_result.schema.json", schema: JournalQueryResultMessageSchema },
  { file: "assembly_reload.schema.json", schema: AssemblyReloadMessageSchema },
  // Client messages
  { file: "eval.schema.json", schema: EvalMessageSchema },
  { file: "complete.schema.json", schema: CompleteMessageSchema },
  { file: "reset.schema.json", schema: ResetMessageSchema },
  { file: "subscribe.schema.json", schema: SubscribeMessageSchema },
  { file: "cancel.schema.json", schema: CancelMessageSchema },
  { file: "commands_list.schema.json", schema: CommandsListMessageSchema },
  { file: "command_describe.schema.json", schema: CommandDescribeMessageSchema },
  { file: "command_call.schema.json", schema: CommandCallMessageSchema },
  { file: "job_status.schema.json", schema: JobStatusMessageSchema },
  { file: "job_cancel.schema.json", schema: JobCancelMessageSchema },
  { file: "journal_query.schema.json", schema: JournalQueryMessageSchema },
  // Shared types
  { file: "error-envelope.schema.json", schema: ErrorEnvelopeSchema },
  { file: "artifact-ref.schema.json", schema: ArtifactRefSchema },
  { file: "command-summary.schema.json", schema: CommandSummarySchema },
  { file: "command-descriptor.schema.json", schema: CommandDescriptorSchema },
  { file: "journal-entry.schema.json", schema: JournalEntrySchema },
  { file: "json-object.schema.json", schema: JsonObjectSchema },
];

for (const { file, schema } of entries) {
  await writeFile(join(schemaDir, file), `${JSON.stringify(schema, null, 2)}\n`);
  console.log(`  wrote ${file}`);
}

console.log(`\nExported ${entries.length} schemas to packages/protocol/schemas/`);
```

- [ ] **Step 2: Run the export script**

```bash
cd ~/Projects/HotRepl
bun run schemas:export
```

Expected output:

```
  wrote handshake.schema.json
  wrote eval_result.schema.json
  ...
  wrote json-object.schema.json

Exported 35 schemas to packages/protocol/schemas/
```

Check that `packages/protocol/schemas/` now contains 35 `.schema.json` files, each starting with `{`
(valid JSON, no TypeBox annotations).

- [ ] **Step 3: Spot-check one schema for cleanliness**

```bash
cd ~/Projects/HotRepl
bun -e "const s = require('./packages/protocol/schemas/eval_result.schema.json'); console.log(JSON.stringify(Object.keys(s)))"
```

Expected: `["additionalProperties","type","required","properties"]` — no `~kind` or TypeBox-internal
keys.

- [ ] **Step 4: Add `assembly_reload` and `cancel` entries to `docs/control-plane-protocol.md`**

Add a new section "Assembly Reload" after the Journal section:

````markdown
## Assembly Reload

Sent by the server when a game assembly is hot-reloaded. Currently not handled by the SDK transport;
clients may observe this as an unmatched server push.

```json
{
  "type": "assembly_reload",
  "assembly": "HotRepl.Plugin.dll",
  "message": "Assembly reload complete."
}
```
````

`assembly` is optional. `message` is always present.

````
Add a subsection under Subscriptions for `cancel`:
```markdown
### Cancel

Cancel an active eval or subscription by its original request `id`.
Not yet sent by the TypeScript SDK; available for custom transports.

```json
{ "type": "cancel", "id": "cancel-1", "targetId": "watch-1" }
````

````
- [ ] **Step 5: Commit**

```bash
cd ~/Projects/HotRepl
git add packages/protocol/scripts/export-schemas.ts packages/protocol/schemas/ docs/control-plane-protocol.md
git commit -m "feat(protocol): export JSON Schema for all 35 message types

Rewrites export-schemas.ts to cover all client, server, and shared-type
schemas. In TypeBox 1.x JSON.stringify already produces clean JSON Schema
(~kind etc. are non-enumerable); no Type.Strict() needed.

Also adds assembly_reload and cancel documentation to
control-plane-protocol.md."
````

---

## Task 7: Site scaffold

**Files:**

- Modify: `package.json` (root)
- Create: `site/package.json`
- Create: `site/tsconfig.json`
- Create: `site/svelte.config.js`
- Create: `site/vite.config.ts`
- Create: `site/wrangler.toml`

- [ ] **Step 1: Add `site` to the root workspace**

In `~/Projects/HotRepl/package.json`, change:

```json
"workspaces": ["packages/*"]
```

to:

```json
"workspaces": ["packages/*", "site"]
```

- [ ] **Step 2: Create `site/package.json`**

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
    "@sveltejs/adapter-cloudflare": "^7.0.0",
    "@sveltejs/kit": "^2.0.0",
    "@sveltejs/vite-plugin-svelte": "^7.0.0",
    "@tailwindcss/vite": "^4.0.0",
    "shiki": "^3.0.0",
    "svelte": "^5.0.0",
    "svelte-check": "^4.0.0",
    "tailwindcss": "^4.0.0",
    "typebox": "^1.0.0",
    "vite": "^8.0.0",
    "wrangler": "^4.0.0"
  }
}
```

- [ ] **Step 3: Create `site/tsconfig.json`**

```json
{
  "extends": "./.svelte-kit/tsconfig.json",
  "compilerOptions": {
    "allowJs": true,
    "checkJs": true,
    "esModuleInterop": true,
    "forceConsistentCasingInFileNames": true,
    "resolveJsonModule": true,
    "skipLibCheck": true,
    "sourceMap": true,
    "strict": true
  }
}
```

- [ ] **Step 4: Create `site/svelte.config.js`**

```javascript
import adapter from "@sveltejs/adapter-cloudflare";
import { vitePreprocess } from "@sveltejs/vite-plugin-svelte";

/** @type {import('@sveltejs/kit').Config} */
const config = {
  preprocess: vitePreprocess(),
  kit: {
    adapter: adapter({}),
    alias: { $lib: "src/lib" },
  },
};

export default config;
```

- [ ] **Step 5: Create `site/vite.config.ts`**

No `optimizeDeps.exclude` needed — Vite automatically treats linked Bun workspace deps as source:

```typescript
import { sveltekit } from "@sveltejs/kit/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [tailwindcss(), sveltekit()],
});
```

- [ ] **Step 6: Create `site/wrangler.toml`**

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

- [ ] **Step 7: Install dependencies**

```bash
cd ~/Projects/HotRepl
bun install
```

Expected: `site/` packages installed, `@hotrepl/protocol` symlinked as workspace dep.

- [ ] **Step 8: Run svelte-kit sync to generate type stubs**

```bash
cd ~/Projects/HotRepl/site
bun run check
```

Expected: may warn about missing routes — that's fine at this stage. The `.svelte-kit/` directory is
created.

- [ ] **Step 9: Commit**

```bash
cd ~/Projects/HotRepl
git add package.json site/package.json site/tsconfig.json site/svelte.config.js site/vite.config.ts site/wrangler.toml bun.lock
git commit -m "feat(site): scaffold SvelteKit + Cloudflare site for hotrepl.glockyco.com

Adds site/ to the Bun workspace. Stack: SvelteKit 2, Svelte 5, Tailwind v4
via @tailwindcss/vite, Shiki v3, adapter-cloudflare. Imports @hotrepl/protocol
from workspace; Vite treats linked ESM workspace deps as source automatically."
```

---

## Task 8: Site design foundation — app shell + landing page

**Files:**

- Create: `site/src/app.html`
- Create: `site/src/app.css`
- Create: `site/src/routes/+layout.svelte`
- Create: `site/src/routes/+page.server.ts`
- Create: `site/src/routes/+page.svelte`

- [ ] **Step 1: Create `site/src/app.html`**

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
    %sveltekit.head%
  </head>
  <body>
    <div style="display: contents">%sveltekit.body%</div>
  </body>
</html>
```

- [ ] **Step 2: Create `site/src/app.css`**

Dark-only palette. `@theme inline {}` maps CSS custom properties to Tailwind utility names.

```css
@import "tailwindcss";

:root {
  --bg:          oklch(0.11 0.01 240);
  --surface:     oklch(0.16 0.01 240);
  --surface-2:   oklch(0.20 0.015 240);
  --border:      oklch(0.28 0.02 240);
  --text:        oklch(0.93 0.01 240);
  --muted:       oklch(0.58 0.02 240);
  --accent:      oklch(0.75 0.18 45);
  --accent-dim:  oklch(0.75 0.18 45 / 15%);
  --badge-cs:    oklch(0.75 0.18 45);
  --badge-sc:    oklch(0.65 0.15 220);
  --code-bg:     oklch(0.13 0.01 240);
  --radius:      6px;
}

@theme inline {
  --color-bg:          var(--bg);
  --color-surface:     var(--surface);
  --color-surface-2:   var(--surface-2);
  --color-border:      var(--border);
  --color-text:        var(--text);
  --color-muted:       var(--muted);
  --color-accent:      var(--accent);
  --color-accent-dim:  var(--accent-dim);
  --color-badge-cs:    var(--badge-cs);
  --color-badge-sc:    var(--badge-sc);
  --color-code-bg:     var(--code-bg);
}

body {
  background: var(--bg);
  color: var(--text);
  font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  line-height: 1.6;
}

/* Shiki output: make code block backgrounds match site palette */
.shiki {
  background-color: var(--code-bg) !important;
  border-radius: var(--radius);
  padding: 1rem 1.25rem;
  overflow-x: auto;
  font-size: 0.875rem;
  line-height: 1.6;
}
```

- [ ] **Step 3: Create `site/src/routes/+layout.svelte`**

```svelte
<script lang="ts">
  import "../app.css";
  let { children } = $props();
</script>

<div class="site-shell">
  <header class="site-header">
    <nav class="nav-inner">
      <a class="wordmark" href="/">HotRepl</a>
      <a class="nav-link" href="/protocol/">Protocol Reference</a>
      <a class="nav-link" href="https://github.com/glockyco/HotRepl" rel="noopener noreferrer">
        GitHub
      </a>
    </nav>
  </header>

  <main class="site-main">
    {@render children()}
  </main>

  <footer class="site-footer">
    <span>By <a href="https://glockyco.com/">Johann Glock</a></span>
    <span>·</span>
    <a href="https://github.com/glockyco/HotRepl">GitHub</a>
  </footer>
</div>

<style>
  .site-shell {
    min-height: 100dvh;
    display: flex;
    flex-direction: column;
  }

  .site-header {
    border-bottom: 1px solid var(--border);
    position: sticky;
    top: 0;
    background: var(--bg);
    z-index: 10;
  }

  .nav-inner {
    max-width: 960px;
    margin: 0 auto;
    padding: 0 24px;
    height: 52px;
    display: flex;
    align-items: center;
    gap: 24px;
  }

  .wordmark {
    font-size: 1rem;
    font-weight: 800;
    color: var(--accent);
    letter-spacing: -0.02em;
    text-decoration: none;
    margin-right: auto;
  }

  .nav-link {
    font-size: 0.875rem;
    font-weight: 600;
    color: var(--muted);
    text-decoration: none;
    transition: color 0.15s;
  }

  .nav-link:hover {
    color: var(--text);
  }

  .site-main {
    flex: 1;
    max-width: 960px;
    margin: 0 auto;
    padding: 0 24px;
    width: 100%;
  }

  .site-footer {
    border-top: 1px solid var(--border);
    padding: 20px 24px;
    display: flex;
    justify-content: center;
    gap: 12px;
    font-size: 0.8125rem;
    color: var(--muted);
  }

  .site-footer a {
    color: var(--muted);
    text-decoration: underline;
    text-underline-offset: 3px;
  }

  .site-footer a:hover {
    color: var(--text);
  }
</style>
```

- [ ] **Step 4: Create `site/src/routes/+page.server.ts`**

Server-only load for the landing page (highlights the quickstart code block with Shiki):

```typescript
import { codeToHtml } from "shiki";

export const prerender = true;

export async function load() {
  const quickstart = `import { connect } from "@hotrepl/sdk";

const session = await connect(); // ws://127.0.0.1:18590 by default
const name = await session.eval("UnityEngine.Application.productName");
const preflight = await session.run("archive.preflight", {});

console.log(name.value, preflight.output);`;

  const quickstartHtml = await codeToHtml(quickstart, {
    lang: "typescript",
    theme: "github-dark",
  });

  return { quickstartHtml };
}
```

- [ ] **Step 5: Create `site/src/routes/+page.svelte`**

```svelte
<script lang="ts">
  import type { PageServerData } from "./$types";
  let { data }: { data: PageServerData } = $props();
</script>

<svelte:head>
  <title>HotRepl — Runtime C# REPL for Unity games</title>
  <meta
    name="description"
    content="Runtime C# REPL and typed command bridge for Unity games. Embed via BepInEx or MelonLoader, inspect and automate a running game from your terminal, scripts, or AI agents."
  />
</svelte:head>

<!-- ── Hero ─────────────────────────────────────────────── -->
<section class="hero">
  <h1 class="hero-title">HotRepl</h1>
  <p class="hero-tagline">Runtime C# REPL and typed command bridge for Unity games</p>
  <p class="hero-desc">
    Embed in any Unity game via BepInEx or MelonLoader. Inspect and automate a running game from
    your terminal, scripts, or AI agents — without rebuilding.
  </p>
  <div class="hero-ctas">
    <a class="btn btn-primary" href="/protocol/">Protocol Reference →</a>
    <a
      class="btn btn-secondary"
      href="https://github.com/glockyco/HotRepl"
      rel="noopener noreferrer"
    >
      GitHub
    </a>
  </div>
</section>

<!-- ── Feature cards ─────────────────────────────────────── -->
<section class="features">
  <div class="feature-card">
    <h3>Raw Eval</h3>
    <p>
      Write C# on the game's main thread. Inspect live objects, run one-off repair snippets,
      explore the runtime interactively.
    </p>
  </div>
  <div class="feature-card">
    <h3>Typed Commands</h3>
    <p>
      Schema-validated operations registered by the host. Stable contract for repeatable exports,
      tests, and agent workflows.
    </p>
  </div>
  <div class="feature-card">
    <h3>Agent &amp; MCP Ready</h3>
    <p>
      Connect via WebSocket SDK, CLI, or the nine-tool MCP stdio server. Plug into any coding
      agent, script, or automation pipeline.
    </p>
  </div>
</section>

<!-- ── Quickstart ─────────────────────────────────────────── -->
<section class="section">
  <h2 class="section-title">Quickstart</h2>
  <!-- eslint-disable-next-line svelte/no-at-html-tags -->
  {@html data.quickstartHtml}
</section>

<!-- ── Integration paths ─────────────────────────────────── -->
<section class="section">
  <h2 class="section-title">Integration paths</h2>
  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>Path</th>
          <th>Use for</th>
          <th>Entry</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>Raw eval</td>
          <td>Interactive inspection, one-off repair</td>
          <td><code>session.eval()</code> / <code>hotrepl eval</code></td>
        </tr>
        <tr>
          <td>Typed commands</td>
          <td>Automation, exports, repeatable tests</td>
          <td><code>session.run()</code> / <code>command_call</code></td>
        </tr>
        <tr>
          <td>CLI</td>
          <td>Shell scripts, local workflows</td>
          <td><code>@hotrepl/cli</code></td>
        </tr>
        <tr>
          <td>MCP</td>
          <td>AI agent tool catalog</td>
          <td><code>@hotrepl/mcp</code></td>
        </tr>
        <tr>
          <td>Host embedding</td>
          <td>New loader adapters, test hosts</td>
          <td><code>IReplHost</code> + <code>ReplEngine.Tick()</code></td>
        </tr>
      </tbody>
    </table>
  </div>
</section>

<!-- ── Real consumers ─────────────────────────────────────── -->
<section class="section">
  <h2 class="section-title">Real consumers</h2>
  <div class="consumers">
    <a
      class="consumer-card"
      href="https://github.com/glockyco/ardenfall-compendium"
      rel="noopener noreferrer"
    >
      <strong>Ardenfall Compendium</strong>
      <span>BepInEx/Mono path — reference consumer for typed commands and snapshot artifacts</span>
    </a>
    <a
      class="consumer-card"
      href="https://github.com/glockyco/ancient-kingdoms-mods"
      rel="noopener noreferrer"
    >
      <strong>Ancient Kingdoms Compendium</strong>
      <span>MelonLoader/IL2CPP path — reference consumer for data export orchestration</span>
    </a>
  </div>
</section>

<style>
  /* ── Hero ── */
  .hero {
    padding: 72px 0 56px;
    max-width: 640px;
  }

  .hero-title {
    font-size: clamp(2.5rem, 6vw, 4rem);
    font-weight: 900;
    letter-spacing: -0.04em;
    color: var(--accent);
    line-height: 1;
    margin-bottom: 16px;
  }

  .hero-tagline {
    font-size: 1.125rem;
    font-weight: 600;
    color: var(--text);
    margin-bottom: 12px;
    line-height: 1.4;
  }

  .hero-desc {
    color: var(--muted);
    font-size: 1rem;
    line-height: 1.7;
    margin-bottom: 28px;
  }

  .hero-ctas {
    display: flex;
    gap: 12px;
    flex-wrap: wrap;
  }

  .btn {
    display: inline-flex;
    align-items: center;
    padding: 10px 22px;
    border-radius: var(--radius);
    font-size: 0.9375rem;
    font-weight: 700;
    text-decoration: none;
    transition: all 0.15s;
  }

  .btn-primary {
    background: var(--accent);
    color: oklch(0.1 0 0);
  }

  .btn-primary:hover {
    filter: brightness(1.1);
  }

  .btn-secondary {
    background: var(--surface);
    color: var(--text);
    border: 1px solid var(--border);
  }

  .btn-secondary:hover {
    border-color: var(--accent);
    color: var(--accent);
  }

  /* ── Features ── */
  .features {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 16px;
    padding: 0 0 48px;
  }

  @media (max-width: 640px) {
    .features {
      grid-template-columns: 1fr;
    }
  }

  .feature-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px 24px;
  }

  .feature-card h3 {
    font-size: 0.9375rem;
    font-weight: 700;
    color: var(--accent);
    margin-bottom: 8px;
  }

  .feature-card p {
    font-size: 0.875rem;
    color: var(--muted);
    line-height: 1.6;
  }

  /* ── Sections ── */
  .section {
    padding: 40px 0;
    border-top: 1px solid var(--border);
  }

  .section-title {
    font-size: 1rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--accent);
    margin-bottom: 20px;
  }

  /* ── Table ── */
  .table-wrap {
    overflow-x: auto;
  }

  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.875rem;
  }

  th {
    text-align: left;
    padding: 8px 12px;
    border-bottom: 1px solid var(--border);
    color: var(--muted);
    font-weight: 600;
    font-size: 0.8125rem;
  }

  td {
    padding: 10px 12px;
    border-bottom: 1px solid var(--border);
    color: var(--text);
    vertical-align: top;
  }

  td:first-child {
    font-weight: 600;
    color: var(--accent);
    white-space: nowrap;
  }

  code {
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.8125rem;
    background: var(--code-bg);
    padding: 2px 6px;
    border-radius: 4px;
    color: var(--text);
  }

  /* ── Consumers ── */
  .consumers {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 12px;
  }

  @media (max-width: 640px) {
    .consumers {
      grid-template-columns: 1fr;
    }
  }

  .consumer-card {
    display: block;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 16px 20px;
    text-decoration: none;
    transition: border-color 0.15s;
  }

  .consumer-card:hover {
    border-color: var(--accent);
  }

  .consumer-card strong {
    display: block;
    font-size: 0.9375rem;
    color: var(--accent);
    margin-bottom: 4px;
  }

  .consumer-card span {
    font-size: 0.8125rem;
    color: var(--muted);
    line-height: 1.5;
  }
</style>
```

- [ ] **Step 6: Build to verify prerender works**

```bash
cd ~/Projects/HotRepl/site
bun run build
```

Expected: build succeeds, `.svelte-kit/cloudflare/` populated. No Shiki errors.

- [ ] **Step 7: Commit**

```bash
cd ~/Projects/HotRepl
git add site/src/
git commit -m "feat(site): landing page with hero, features, quickstart, consumers

Server-only load (+page.server.ts) highlights the TypeScript quickstart
with Shiki at prerender time — Shiki never enters the client bundle.
Dark-only palette (oklch, warm orange accent). Layout includes sticky
top nav and footer."
```

---

## Task 9: Protocol data file

**Files:**

- Create: `site/src/lib/data/protocol.ts`

This is the single source of truth for the protocol reference content. Every example is validated
against the corresponding TypeBox schema at build time in the server load (Task 10).

- [ ] **Step 1: Create `site/src/lib/data/protocol.ts`**

```typescript
import {
  ArtifactRefSchema,
  AssemblyReloadMessageSchema,
  CancelMessageSchema,
  CommandCallMessageSchema,
  CommandDescribeMessageSchema,
  CommandDescribeResultMessageSchema,
  CommandDescriptorSchema,
  CommandResultMessageSchema,
  CommandsListMessageSchema,
  CommandsListResultMessageSchema,
  CompleteMessageSchema,
  CompleteResultMessageSchema,
  // Shared type schemas
  ErrorEnvelopeSchema,
  EvalErrorMessageSchema,
  // Client message schemas
  EvalMessageSchema,
  EvalResultMessageSchema,
  // Server message schemas
  HandshakeMessageSchema,
  JobAcceptedMessageSchema,
  JobCancelMessageSchema,
  JobCancelResultMessageSchema,
  JobResultMessageSchema,
  JobStatusMessageSchema,
  JobStatusResultMessageSchema,
  JournalEntrySchema,
  JournalQueryMessageSchema,
  JournalQueryResultMessageSchema,
  MESSAGE_TYPES,
  type MessageType,
  ProtocolErrorMessageSchema,
  ResetMessageSchema,
  ResetResultMessageSchema,
  SessionEvictedMessageSchema,
  SubscribeErrorMessageSchema,
  SubscribeMessageSchema,
  SubscribeResultMessageSchema,
} from "@hotrepl/protocol";
import type { TSchema } from "typebox";
import { Value } from "typebox/value";

export type Direction = "C→S" | "S→C";

export interface MessageDef {
  type: MessageType; // enforces that only valid wire discriminants are used
  direction: Direction;
  description: string; // one sentence
  example: string; // raw JSON — validated against schema at build time
  schema: TSchema;
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

// ── Runtime exhaustiveness assertion ─────────────────────────────────────────
// Runs at module load time (= prerender time). Build fails with a clear message
// if any MESSAGE_TYPES discriminant is absent from the families array.
// This is more reliable than the broken type-level approach because families[]
// widens MessageDef.type to string when annotated as MessageFamily[].
export function assertExhaustive(f: MessageFamily[]): void {
  const documented = new Set(f.flatMap((fam) => fam.messages.map((m) => m.type)));
  const missing = (Object.values(MESSAGE_TYPES) as string[]).filter((t) => !documented.has(t));
  if (missing.length > 0) {
    throw new Error(
      `Protocol reference is missing documentation for: ${missing.join(", ")}. `
        + `Add entries to site/src/lib/data/protocol.ts.`,
    );
  }
}

// ── Validation helper ─────────────────────────────────────────────────────────
// Called from +page.server.ts. Throws on any invalid example.
export function validateAllExamples(f: MessageFamily[], shared: SharedTypeDef[]): void {
  for (const family of f) {
    for (const msg of family.messages) {
      const parsed: unknown = JSON.parse(msg.example);
      if (!Value.Check(msg.schema, parsed)) {
        const errors = Value.Errors(msg.schema, parsed);
        throw new Error(
          `Example for '${msg.type}' fails schema validation:\n`
            + JSON.stringify(errors, null, 2),
        );
      }
    }
  }
  for (const t of shared) {
    const parsed: unknown = JSON.parse(t.example);
    if (!Value.Check(t.schema, parsed)) {
      throw new Error(`Example for shared type '${t.name}' fails schema validation.`);
    }
  }
}

// ── Data ─────────────────────────────────────────────────────────────────────

export const families: MessageFamily[] = [
  {
    id: "connection",
    name: "Connection",
    description: "Messages exchanged when a WebSocket connection opens or the session changes.",
    messages: [
      {
        type: MESSAGE_TYPES.handshake,
        direction: "S→C",
        description:
          "Sent immediately after the WebSocket opens. Advertises host identity, evaluator capabilities, runtime limits, and typed-command support.",
        schema: HandshakeMessageSchema,
        example: `{
  "type": "handshake",
  "protocolVersion": 2,
  "host": { "name": "BepInEx", "version": "0.x", "platform": "Unity Mono" },
  "evaluator": {
    "name": "Mono.CSharp",
    "languageVersion": "7.x",
    "persistentState": true,
    "supportsCompletion": true,
    "cancellation": "hardAbort"
  },
  "availableEvaluators": ["Mono.CSharp"],
  "defaultUsings": ["System"],
  "helpers": ["String[] Help()"],
  "control": { "supported": true, "commandsListChanged": false, "schemaValidation": false },
  "limits": {
    "maxMessageBytes": 4194304,
    "maxQueuedCommands": 32,
    "maxResultLength": 102400,
    "maxEnumerableElements": 100,
    "defaultEvalTimeoutMs": 10000,
    "maxJobConcurrency": 1
  },
  "enforces": ["maxMessageBytes", "maxQueuedCommands", "maxResultLength",
               "maxEnumerableElements", "maxJobConcurrency"]
}`,
      },
      {
        type: MESSAGE_TYPES.sessionEvicted,
        direction: "S→C",
        description:
          "Sent to the previous client when a new WebSocket connection replaces it. Active subscriptions are closed.",
        schema: SessionEvictedMessageSchema,
        example: `{ "type": "session_evicted", "reason": "new_connection" }`,
      },
      {
        type: MESSAGE_TYPES.assemblyReload,
        direction: "S→C",
        description:
          "Sent when the game hot-reloads an assembly. Currently not routed by the SDK transport; clients observe this as an unsolicited push.",
        schema: AssemblyReloadMessageSchema,
        example:
          `{ "type": "assembly_reload", "assembly": "HotRepl.Plugin.dll", "message": "Assembly reload complete." }`,
      },
      {
        type: MESSAGE_TYPES.error,
        direction: "S→C",
        description:
          "Protocol-level error not attributable to a specific request. Has an optional id when a request triggered it.",
        schema: ProtocolErrorMessageSchema,
        example: `{
  "type": "error",
  "error": {
    "kind": "invalid_request",
    "code": "malformedJson",
    "message": "Could not parse the incoming JSON frame.",
    "retryable": false
  }
}`,
      },
    ],
  },
  {
    id: "eval",
    name: "Eval",
    description: "C# expression evaluation on the game's main thread.",
    messages: [
      {
        type: MESSAGE_TYPES.eval,
        direction: "C→S",
        description:
          "Submit a C# expression for evaluation. The evaluator state persists between evals until reset.",
        schema: EvalMessageSchema,
        example: `{ "type": "eval", "id": "eval-1", "code": "1 + 1", "timeoutMs": 10000 }`,
      },
      {
        type: MESSAGE_TYPES.evalResult,
        direction: "S→C",
        description:
          "Returned when the expression completes without a runtime exception. Matched to the request by id.",
        schema: EvalResultMessageSchema,
        example: `{
  "type": "eval_result",
  "id": "eval-1",
  "hasValue": true,
  "value": "2",
  "valueType": "System.Int32",
  "durationMs": 3
}`,
      },
      {
        type: MESSAGE_TYPES.evalError,
        direction: "S→C",
        description:
          "Returned when the expression throws or the evaluator reports a compile error.",
        schema: EvalErrorMessageSchema,
        example: `{
  "type": "eval_error",
  "id": "eval-1",
  "error": {
    "kind": "internal",
    "code": "runtimeException",
    "message": "NullReferenceException: Object reference not set to an instance of an object.",
    "retryable": false
  }
}`,
      },
      {
        type: MESSAGE_TYPES.complete,
        direction: "C→S",
        description: "Request code-completion candidates for a partial expression.",
        schema: CompleteMessageSchema,
        example: `{ "type": "complete", "id": "c-1", "code": "UnityEngine.Application.pro" }`,
      },
      {
        type: MESSAGE_TYPES.completeResult,
        direction: "S→C",
        description: "Completion candidates for the submitted partial expression.",
        schema: CompleteResultMessageSchema,
        example: `{
  "type": "complete_result",
  "id": "c-1",
  "completions": ["productName", "productVersion", "platform"],
  "durationMs": 5
}`,
      },
      {
        type: MESSAGE_TYPES.reset,
        direction: "C→S",
        description: "Clear all persistent evaluator variables and type definitions.",
        schema: ResetMessageSchema,
        example: `{ "type": "reset", "id": "r-1" }`,
      },
      {
        type: MESSAGE_TYPES.resetResult,
        direction: "S→C",
        description: "Confirmation that the evaluator state has been cleared.",
        schema: ResetResultMessageSchema,
        example: `{ "type": "reset_result", "id": "r-1", "success": true }`,
      },
    ],
  },
  {
    id: "subscriptions",
    name: "Subscriptions",
    description: "Repeating evals that run on a per-frame interval.",
    messages: [
      {
        type: MESSAGE_TYPES.subscribe,
        direction: "C→S",
        description:
          "Start a frame subscription. The server evaluates code every intervalFrames frames and streams results until limit is reached or the subscription is cancelled.",
        schema: SubscribeMessageSchema,
        example: `{
  "type": "subscribe",
  "id": "watch-1",
  "code": "Time.frameCount",
  "intervalFrames": 1,
  "limit": 10
}`,
      },
      {
        type: MESSAGE_TYPES.subscribeResult,
        direction: "S→C",
        description: "One tick of a running subscription. final: true on the last tick.",
        schema: SubscribeResultMessageSchema,
        example: `{
  "type": "subscribe_result",
  "id": "watch-1",
  "seq": 0,
  "hasValue": true,
  "value": "42",
  "valueType": "System.Int32",
  "durationMs": 3,
  "final": false
}`,
      },
      {
        type: MESSAGE_TYPES.subscribeError,
        direction: "S→C",
        description:
          "Subscription tick that produced an error. final: true terminates the subscription.",
        schema: SubscribeErrorMessageSchema,
        example: `{
  "type": "subscribe_error",
  "id": "watch-1",
  "seq": 0,
  "error": {
    "kind": "timeout",
    "code": "evalTimeout",
    "message": "Eval timed out after 10000 ms.",
    "retryable": false
  },
  "final": true
}`,
      },
      {
        type: MESSAGE_TYPES.cancel,
        direction: "C→S",
        description:
          "Cancel an active eval or subscription by its request id. Not yet sent by the TypeScript SDK RuntimeRequest; available for custom transports.",
        schema: CancelMessageSchema,
        example: `{ "type": "cancel", "id": "cancel-1", "targetId": "watch-1" }`,
      },
    ],
  },
  {
    id: "typed-commands",
    name: "Typed Commands",
    description: "Schema-validated operations registered by the host.",
    messages: [
      {
        type: MESSAGE_TYPES.commandsList,
        direction: "C→S",
        description: "List all commands currently registered by the host.",
        schema: CommandsListMessageSchema,
        example: `{ "type": "commands_list", "id": "list-1" }`,
      },
      {
        type: MESSAGE_TYPES.commandsListResult,
        direction: "S→C",
        description: "The full command catalog.",
        schema: CommandsListResultMessageSchema,
        example: `{
  "type": "commands_list_result",
  "id": "list-1",
  "commands": [
    { "name": "archive.preflight", "majorVersion": 1, "kind": "sync", "mutatesState": false },
    { "name": "archive.export", "majorVersion": 1, "kind": "job", "mutatesState": true }
  ]
}`,
      },
      {
        type: MESSAGE_TYPES.commandDescribe,
        direction: "C→S",
        description: "Fetch the full descriptor for one command, including I/O schemas.",
        schema: CommandDescribeMessageSchema,
        example: `{ "type": "command_describe", "id": "describe-1", "name": "archive.preflight" }`,
      },
      {
        type: MESSAGE_TYPES.commandDescribeResult,
        direction: "S→C",
        description:
          "Full command descriptor including JSON schemas for input, output, and artifacts.",
        schema: CommandDescribeResultMessageSchema,
        example: `{
  "type": "command_describe_result",
  "id": "describe-1",
  "descriptor": {
    "name": "archive.preflight",
    "majorVersion": 1,
    "kind": "sync",
    "mutatesState": false,
    "inputSchema": { "type": "object", "properties": {} },
    "outputSchema": { "type": "object", "properties": { "ok": { "type": "boolean" } } },
    "artifactsSchema": { "type": "object" }
  }
}`,
      },
      {
        type: MESSAGE_TYPES.commandCall,
        direction: "C→S",
        description: "Execute a registered command. args must be a JSON object.",
        schema: CommandCallMessageSchema,
        example:
          `{ "type": "command_call", "id": "cmd-1", "name": "archive.preflight", "args": {} }`,
      },
      {
        type: MESSAGE_TYPES.commandResult,
        direction: "S→C",
        description:
          "Result for synchronous commands and failed jobs. status ok or failed; error present on failure.",
        schema: CommandResultMessageSchema,
        example: `{
  "type": "command_result",
  "id": "cmd-1",
  "status": "ok",
  "output": { "ok": true },
  "artifacts": {},
  "durationMs": 12
}`,
      },
    ],
  },
  {
    id: "jobs",
    name: "Jobs",
    description: "Long-running async commands polled by the client.",
    messages: [
      {
        type: MESSAGE_TYPES.jobAccepted,
        direction: "S→C",
        description: "A job command was accepted and is now running. Poll with job_status.",
        schema: JobAcceptedMessageSchema,
        example: `{ "type": "job_accepted", "id": "cmd-1", "jobId": "job-1", "state": "running" }`,
      },
      {
        type: MESSAGE_TYPES.jobStatus,
        direction: "C→S",
        description: "Poll a running job for progress or terminal result.",
        schema: JobStatusMessageSchema,
        example: `{ "type": "job_status", "id": "status-1", "jobId": "job-1" }`,
      },
      {
        type: MESSAGE_TYPES.jobStatusResult,
        direction: "S→C",
        description: "Job is still running. Continue polling.",
        schema: JobStatusResultMessageSchema,
        example:
          `{ "type": "job_status_result", "id": "status-1", "jobId": "job-1", "state": "running" }`,
      },
      {
        type: MESSAGE_TYPES.jobResult,
        direction: "S→C",
        description:
          "Terminal job result. Returned in place of job_status_result once the job is done, failed, or cancelled.",
        schema: JobResultMessageSchema,
        example: `{
  "type": "job_result",
  "id": "status-2",
  "jobId": "job-1",
  "state": "done",
  "status": "ok",
  "output": { "itemsExported": 1500 },
  "artifacts": {},
  "durationMs": 1842
}`,
      },
      {
        type: MESSAGE_TYPES.jobCancel,
        direction: "C→S",
        description: "Request cancellation of a running job.",
        schema: JobCancelMessageSchema,
        example: `{ "type": "job_cancel", "id": "jc-1", "jobId": "job-1" }`,
      },
      {
        type: MESSAGE_TYPES.jobCancelResult,
        direction: "S→C",
        description:
          "Cancellation acknowledgement. accepted indicates whether the runtime accepted the request.",
        schema: JobCancelResultMessageSchema,
        example:
          `{ "type": "job_cancel_result", "id": "jc-1", "accepted": true, "state": "running" }`,
      },
    ],
  },
  {
    id: "journal",
    name: "Journal",
    description: "Queryable history of recent eval and command activity.",
    messages: [
      {
        type: MESSAGE_TYPES.journalQuery,
        direction: "C→S",
        description: "Query recent eval and command journal entries.",
        schema: JournalQueryMessageSchema,
        example: `{ "type": "journal_query", "id": "journal-1", "kind": "command", "limit": 20 }`,
      },
      {
        type: MESSAGE_TYPES.journalQueryResult,
        direction: "S→C",
        description: "Recent journal entries, newest first.",
        schema: JournalQueryResultMessageSchema,
        example: `{
  "type": "journal_query_result",
  "id": "journal-1",
  "entries": [
    {
      "id": "cmd-1",
      "kind": "command",
      "name": "archive.preflight",
      "success": true,
      "durationMs": 12,
      "timestamp": "2026-05-23T12:00:00.000Z"
    }
  ]
}`,
      },
    ],
  },
];

// Run exhaustiveness check at module load time.
assertExhaustive(families);

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
  {
    name: "ArtifactRef",
    description:
      "Named reference to a file produced by a command. Consumers must verify sha256 before trusting content.",
    schema: ArtifactRefSchema,
    example: `{
  "uri": "file:///exports/items.json",
  "path": "/exports/items.json",
  "sha256": "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b",
  "byteSize": 48392,
  "contentType": "application/json",
  "finalized": true
}`,
  },
  {
    name: "CommandDescriptor",
    description:
      "Full metadata for a registered typed command including JSON schemas for its input and output.",
    schema: CommandDescriptorSchema,
    example: `{
  "name": "archive.preflight",
  "majorVersion": 1,
  "kind": "sync",
  "mutatesState": false,
  "inputSchema": { "type": "object", "properties": {} },
  "outputSchema": { "type": "object", "properties": { "ok": { "type": "boolean" } } },
  "artifactsSchema": { "type": "object" }
}`,
  },
  {
    name: "JournalEntry",
    description: "One record in the eval/command history.",
    schema: JournalEntrySchema,
    example: `{
  "id": "eval-1",
  "kind": "eval",
  "code": "1 + 1",
  "success": true,
  "durationMs": 3,
  "timestamp": "2026-05-23T12:00:00.000Z"
}`,
  },
];
```

- [ ] **Step 2: Run typecheck**

```bash
cd ~/Projects/HotRepl/site
bun run check
```

Expected: no type errors. The `assertExhaustive` call may produce a runtime error if any
`MESSAGE_TYPES` value was accidentally omitted — that's the intended behavior.

- [ ] **Step 3: Commit**

```bash
cd ~/Projects/HotRepl
git add site/src/lib/data/protocol.ts
git commit -m "feat(site): protocol reference data — all 29 messages + 4 shared types

Complete MessageDef entries for every MESSAGE_TYPES discriminant.
Runtime assertExhaustive() call at module load fails the build if any
wire type is undocumented. validateAllExamples() validates each JSON
example against its TypeBox schema in the server load function.

command_call.args validated as JsonObjectSchema (not Type.Unknown)
per the C# runtime contract. cancel entry sourced from C# CancelMessage
(not in control-plane-protocol.md which previously omitted it)."
```

---

## Task 10: Protocol reference page + components

**Files:**

- Create: `site/src/routes/protocol/+page.server.ts`
- Create: `site/src/routes/protocol/+page.svelte`
- Create: `site/src/lib/components/MessageCard.svelte`
- Create: `site/src/lib/components/ProtocolNav.svelte`

- [ ] **Step 1: Create `site/src/routes/protocol/+page.server.ts`**

Validates all examples and highlights all code with Shiki. Server-only — Shiki never enters the
client bundle.

```typescript
import { families, sharedTypes, validateAllExamples } from "$lib/data/protocol";
import { codeToHtml } from "shiki";
import type { PageServerLoad } from "./$types";

export const prerender = true;

export const load: PageServerLoad = async () => {
  // Fail the build if any example is structurally invalid.
  validateAllExamples(families, sharedTypes);

  const highlightJson = (code: string) => codeToHtml(code, { lang: "json", theme: "github-dark" });

  const highlightedFamilies = await Promise.all(
    families.map(async (family) => ({
      ...family,
      messages: await Promise.all(
        family.messages.map(async (msg) => ({
          ...msg,
          exampleHtml: await highlightJson(msg.example),
          // JSON.stringify produces clean JSON Schema in TypeBox 1.x (non-enumerable internals)
          schemaHtml: await highlightJson(JSON.stringify(msg.schema, null, 2)),
        })),
      ),
    })),
  );

  const highlightedSharedTypes = await Promise.all(
    sharedTypes.map(async (t) => ({
      ...t,
      exampleHtml: await highlightJson(t.example),
      schemaHtml: await highlightJson(JSON.stringify(t.schema, null, 2)),
    })),
  );

  return {
    families: highlightedFamilies,
    sharedTypes: highlightedSharedTypes,
  };
};
```

- [ ] **Step 2: Create `site/src/lib/components/MessageCard.svelte`**

```svelte
<script lang="ts">
  interface Props {
    type: string;
    direction: "C→S" | "S→C";
    description: string;
    exampleHtml: string;
    schemaHtml: string;
  }
  let { type, direction, description, exampleHtml, schemaHtml }: Props = $props();
</script>

<article class="card" id={type}>
  <div class="card-header">
    <code class="type-badge">{type}</code>
    <span class="direction-badge" class:cs={direction === "C→S"} class:sc={direction === "S→C"}>
      {direction}
    </span>
    <span class="version-tag">v2</span>
  </div>

  <p class="description">{description}</p>

  <div class="block-label">Example</div>
  <!-- eslint-disable-next-line svelte/no-at-html-tags -->
  {@html exampleHtml}

  <details class="schema-details">
    <summary>JSON Schema</summary>
    <!-- eslint-disable-next-line svelte/no-at-html-tags -->
    {@html schemaHtml}
  </details>
</article>

<style>
  .card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px 24px;
    margin-bottom: 16px;
    scroll-margin-top: 72px;
  }

  .card-header {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 12px;
    flex-wrap: wrap;
  }

  .type-badge {
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.9375rem;
    font-weight: 700;
    color: var(--accent);
    background: var(--accent-dim);
    padding: 3px 10px;
    border-radius: 4px;
  }

  .direction-badge {
    font-size: 0.75rem;
    font-weight: 700;
    padding: 2px 8px;
    border-radius: 4px;
    letter-spacing: 0.02em;
  }

  .direction-badge.cs {
    background: oklch(0.75 0.18 45 / 20%);
    color: var(--badge-cs);
  }

  .direction-badge.sc {
    background: oklch(0.65 0.15 220 / 20%);
    color: var(--badge-sc);
  }

  .version-tag {
    font-size: 0.75rem;
    color: var(--muted);
    margin-left: auto;
  }

  .description {
    font-size: 0.875rem;
    color: var(--muted);
    line-height: 1.6;
    margin-bottom: 16px;
  }

  .block-label {
    font-size: 0.75rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--muted);
    margin-bottom: 8px;
  }

  .schema-details {
    margin-top: 12px;
  }

  .schema-details summary {
    font-size: 0.8125rem;
    font-weight: 600;
    color: var(--muted);
    cursor: pointer;
    padding: 6px 0;
    user-select: none;
    list-style: none;
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .schema-details summary::before {
    content: "▶";
    font-size: 0.625rem;
    transition: transform 0.15s;
  }

  .schema-details[open] summary::before {
    transform: rotate(90deg);
  }

  .schema-details summary:hover {
    color: var(--text);
  }

  .schema-details :global(.shiki) {
    margin-top: 8px;
  }
</style>
```

- [ ] **Step 3: Create `site/src/lib/components/ProtocolNav.svelte`**

```svelte
<script lang="ts">
  interface Family {
    id: string;
    name: string;
    messages: { type: string }[];
  }

  interface Props {
    families: Family[];
  }

  let { families }: Props = $props();
</script>

<nav class="proto-nav" aria-label="Protocol reference navigation">
  <div class="nav-header">
    <a class="back-link" href="/">← HotRepl</a>
    <span class="nav-title">Protocol Reference</span>
  </div>

  {#each families as family (family.id)}
    <div class="family-group">
      <a class="family-name" href="#{family.messages[0]?.type ?? family.id}">{family.name}</a>
      <ul class="msg-list">
        {#each family.messages as msg (msg.type)}
          <li>
            <a class="msg-link" href="#{msg.type}">{msg.type}</a>
          </li>
        {/each}
      </ul>
    </div>
  {/each}

  <div class="family-group">
    <a class="family-name" href="#shared-types">Shared Types</a>
  </div>
</nav>

<style>
  .proto-nav {
    width: 240px;
    flex-shrink: 0;
    position: sticky;
    top: 52px; /* height of site header */
    height: calc(100dvh - 52px);
    overflow-y: auto;
    padding: 20px 0;
    border-right: 1px solid var(--border);
    scrollbar-width: thin;
  }

  .nav-header {
    padding: 0 16px 16px;
    border-bottom: 1px solid var(--border);
    margin-bottom: 12px;
  }

  .back-link {
    display: block;
    font-size: 0.8125rem;
    color: var(--muted);
    text-decoration: none;
    margin-bottom: 8px;
  }

  .back-link:hover {
    color: var(--text);
  }

  .nav-title {
    font-size: 0.8125rem;
    font-weight: 700;
    color: var(--text);
    letter-spacing: 0.02em;
  }

  .family-group {
    margin-bottom: 4px;
  }

  .family-name {
    display: block;
    padding: 5px 16px;
    font-size: 0.8125rem;
    font-weight: 700;
    color: var(--text);
    text-decoration: none;
    letter-spacing: 0.01em;
  }

  .family-name:hover {
    color: var(--accent);
  }

  .msg-list {
    list-style: none;
    margin: 0;
    padding: 0;
  }

  .msg-link {
    display: block;
    padding: 3px 16px 3px 28px;
    font-size: 0.8125rem;
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    color: var(--muted);
    text-decoration: none;
    transition: color 0.1s;
  }

  .msg-link:hover {
    color: var(--accent);
  }
</style>
```

- [ ] **Step 4: Create `site/src/routes/protocol/+page.svelte`**

```svelte
<script lang="ts">
  import MessageCard from "$lib/components/MessageCard.svelte";
  import ProtocolNav from "$lib/components/ProtocolNav.svelte";
  import type { PageServerData } from "./$types";

  let { data }: { data: PageServerData } = $props();
</script>

<svelte:head>
  <title>Protocol Reference — HotRepl</title>
  <meta
    name="description"
    content="Complete reference for the HotRepl v2 WebSocket protocol — all message types, JSON examples, and JSON Schemas."
  />
</svelte:head>

<div class="layout">
  <!-- Sidebar: hidden on mobile, shown as sticky sidebar on desktop -->
  <div class="sidebar-wrap">
    <ProtocolNav families={data.families} />
  </div>

  <!-- Mobile nav (collapsible) -->
  <details class="mobile-nav">
    <summary>Protocol navigation</summary>
    <ProtocolNav families={data.families} />
  </details>

  <div class="content">
    <div class="content-header">
      <h1>Protocol Reference</h1>
      <p class="content-desc">
        HotRepl v2 — one WebSocket connection, JSON frames, all operations.
      </p>
    </div>

    {#each data.families as family (family.id)}
      <section class="family-section" id={family.id}>
        <h2 class="family-heading">{family.name}</h2>
        <p class="family-desc">{family.description}</p>

        {#each family.messages as msg (msg.type)}
          <MessageCard
            type={msg.type}
            direction={msg.direction}
            description={msg.description}
            exampleHtml={msg.exampleHtml}
            schemaHtml={msg.schemaHtml}
          />
        {/each}
      </section>
    {/each}

    <!-- Shared types -->
    <section class="family-section" id="shared-types">
      <h2 class="family-heading">Shared Types</h2>
      <p class="family-desc">
        Reusable structures referenced by multiple messages.
      </p>

      {#each data.sharedTypes as t (t.name)}
        <article class="card" id={t.name}>
          <div class="card-header">
            <code class="type-badge">{t.name}</code>
          </div>
          <p class="description">{t.description}</p>
          <div class="block-label">Example</div>
          <!-- eslint-disable-next-line svelte/no-at-html-tags -->
          {@html t.exampleHtml}
          <details class="schema-details">
            <summary>JSON Schema</summary>
            <!-- eslint-disable-next-line svelte/no-at-html-tags -->
            {@html t.schemaHtml}
          </details>
        </article>
      {/each}
    </section>
  </div>
</div>

<style>
  .layout {
    display: flex;
    gap: 0;
    /* Override site-main padding for full-bleed sidebar layout */
    margin: 0 -24px;
  }

  .sidebar-wrap {
    display: none;
  }

  @media (min-width: 768px) {
    .sidebar-wrap {
      display: block;
    }

    .mobile-nav {
      display: none;
    }
  }

  .mobile-nav {
    padding: 12px 0;
    border-bottom: 1px solid var(--border);
    margin-bottom: 24px;
  }

  .mobile-nav summary {
    font-size: 0.875rem;
    font-weight: 700;
    color: var(--accent);
    cursor: pointer;
    padding: 0 24px;
    list-style: none;
  }

  .mobile-nav :global(.proto-nav) {
    position: static;
    height: auto;
    width: 100%;
    border-right: none;
  }

  .content {
    flex: 1;
    padding: 32px 32px 64px;
    min-width: 0;
  }

  .content-header {
    margin-bottom: 40px;
  }

  h1 {
    font-size: 1.75rem;
    font-weight: 900;
    letter-spacing: -0.03em;
    color: var(--text);
    margin-bottom: 8px;
  }

  .content-desc {
    font-size: 0.9375rem;
    color: var(--muted);
  }

  .family-section {
    padding: 32px 0;
    border-top: 1px solid var(--border);
    scroll-margin-top: 72px;
  }

  .family-heading {
    font-size: 1.125rem;
    font-weight: 800;
    color: var(--accent);
    letter-spacing: -0.01em;
    margin-bottom: 6px;
  }

  .family-desc {
    font-size: 0.875rem;
    color: var(--muted);
    margin-bottom: 20px;
  }

  /* Shared type cards reuse MessageCard styling inline */
  .card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px 24px;
    margin-bottom: 16px;
    scroll-margin-top: 72px;
  }

  .card-header {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 12px;
  }

  .type-badge {
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.9375rem;
    font-weight: 700;
    color: var(--accent);
    background: var(--accent-dim);
    padding: 3px 10px;
    border-radius: 4px;
  }

  .description {
    font-size: 0.875rem;
    color: var(--muted);
    line-height: 1.6;
    margin-bottom: 16px;
  }

  .block-label {
    font-size: 0.75rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--muted);
    margin-bottom: 8px;
  }

  .schema-details {
    margin-top: 12px;
  }

  .schema-details summary {
    font-size: 0.8125rem;
    font-weight: 600;
    color: var(--muted);
    cursor: pointer;
    padding: 6px 0;
    user-select: none;
    list-style: none;
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .schema-details summary::before {
    content: "▶";
    font-size: 0.625rem;
    transition: transform 0.15s;
  }

  .schema-details[open] summary::before {
    transform: rotate(90deg);
  }
</style>
```

- [ ] **Step 5: Build the full site**

```bash
cd ~/Projects/HotRepl/site
bun run build
```

Expected: build succeeds. Both `/` and `/protocol/` are prerendered (listed in build output). No
Shiki errors. `validateAllExamples` and `assertExhaustive` run silently (no output = success).

If `assertExhaustive` throws during build, the error message will name the missing `MESSAGE_TYPES`
value. Add the missing entry to `protocol.ts` and re-run.

- [ ] **Step 6: Preview locally**

```bash
cd ~/Projects/HotRepl/site
bun run preview
```

Open `http://localhost:4173`. Verify:

- Landing page renders with hero, feature cards, highlighted quickstart, integration table, consumer
  cards.
- `/protocol/` renders with left sidebar, all 6 message families + shared types, message cards with
  direction badges, collapsible JSON Schema blocks.

- [ ] **Step 7: Commit**

```bash
cd ~/Projects/HotRepl
git add site/src/routes/protocol/ site/src/lib/components/
git commit -m "feat(site): protocol reference page with MessageCard and ProtocolNav

Prerendered via +page.server.ts (Shiki stays server-only). All 29
message examples validated against TypeBox schemas at build time.
Two-column layout: sticky sidebar nav + scrollable card content.
Collapsible JSON Schema view per message."
```

---

## Task 11: DNS setup + deploy

- [ ] **Step 1: Add `hotrepl.glockyco.com` as a custom hostname in Cloudflare**

In the Cloudflare dashboard for the `glockyco.com` zone:

1. Go to **DNS** → add a CNAME record: `hotrepl` → your Cloudflare Workers subdomain (e.g.,
   `hotrepl-site.workers.dev`) — or let `wrangler deploy` create it.
2. Alternatively, `wrangler deploy` with `custom_domain = true` handles the route registration
   automatically.

- [ ] **Step 2: Authenticate wrangler if not already done**

```bash
cd ~/Projects/HotRepl/site
bun x wrangler login
```

- [ ] **Step 3: Deploy**

```bash
cd ~/Projects/HotRepl/site
bun run cf-deploy
```

Expected output includes:

```
✨ Successfully deployed to https://hotrepl.glockyco.com
```

- [ ] **Step 4: Verify the live site**

```bash
curl -I https://hotrepl.glockyco.com/
```

Expected: `HTTP/2 200` with `content-type: text/html`.

Open `https://hotrepl.glockyco.com/protocol/` in a browser. Confirm the protocol reference renders.

- [ ] **Step 5: Commit the deployment record**

```bash
cd ~/Projects/HotRepl
git add site/wrangler.toml  # if wrangler modified it
git commit -m "deploy(site): hotrepl.glockyco.com live on Cloudflare Workers"
```

---

## Task 12: Personal website integration

**Files:**

- Modify: `~/Projects/personal-website/src/lib/data/projects.ts`
- Modify: `~/Projects/personal-website/src/lib/assets/screenshots/index.ts`
- Generate: `~/Projects/personal-website/src/lib/assets/screenshots/hotrepl-{thumb,hero}.webp`

- [ ] **Step 1: Add the HotRepl project entry to `projects.ts`**

In `~/Projects/personal-website/src/lib/data/projects.ts`, insert a new object between the
`personal-website` and `10-man-idle` entries in the `rawProjects` array:

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

- [ ] **Step 2: Run typecheck to confirm the entry validates**

```bash
cd ~/Projects/personal-website
pnpm check
```

Expected: no errors.

- [ ] **Step 3: Run the screenshot script**

```bash
cd ~/Projects/personal-website
pnpm screenshots
```

Expected output includes:

```
Capturing 6 project screenshot(s)...
  hotrepl: https://hotrepl.glockyco.com/?theme=dark
    thumb: XX.X kB  hero: XX.X kB
...
Done. Screenshots written to src/lib/assets/screenshots/
```

Verify `src/lib/assets/screenshots/hotrepl-thumb.webp` and `hotrepl-hero.webp` exist.

- [ ] **Step 4: Update `src/lib/assets/screenshots/index.ts`**

Add imports and map entries for both hotrepl screenshots:

```typescript
import hotreplHero from "./hotrepl-hero.webp";
import hotreplThumb from "./hotrepl-thumb.webp";
```

Add to both `thumbnails` and `heroes` records:

```typescript
export const thumbnails: Record<string, string> = {
  // ... existing entries ...
  "hotrepl": hotreplThumb,
};

export const heroes: Record<string, string> = {
  // ... existing entries ...
  "hotrepl": hotreplHero,
};
```

- [ ] **Step 5: Build the personal website to confirm everything compiles**

```bash
cd ~/Projects/personal-website
pnpm build
```

Expected: build succeeds. The `/projects/` page now includes HotRepl between Personal Website and
10-Man Codex. The `/projects/hotrepl/` detail page renders with the live iframe.

- [ ] **Step 6: Commit**

```bash
cd ~/Projects/personal-website
git add src/lib/data/projects.ts src/lib/assets/screenshots/index.ts src/lib/assets/screenshots/hotrepl-thumb.webp src/lib/assets/screenshots/hotrepl-hero.webp
git commit -m "feat: add HotRepl to projects

Positioned between personal-website and 10-man-idle. featured=false,
inPdfCv=false. liveUrl=https://hotrepl.glockyco.com/ (real site,
screenshot captured via existing script). techStack covers C# host,
TypeScript SDK/CLI/MCP, and SvelteKit docs site."
```

---

## Self-Review

**Spec coverage check:**

| Spec section                                      | Task    |
| ------------------------------------------------- | ------- |
| TypeBox upgrade 0.34→1.x                          | Task 1  |
| Shared type schemas                               | Task 2  |
| Server message schemas (all 17 + assembly_reload) | Task 3  |
| Client message schemas (all 11)                   | Task 4  |
| Protocol tests                                    | Task 5  |
| Schema export script                              | Task 6  |
| Site scaffold                                     | Task 7  |
| Landing page + design foundation                  | Task 8  |
| Protocol data file                                | Task 9  |
| Protocol reference page + components              | Task 10 |
| Deploy                                            | Task 11 |
| Personal website entry + screenshots              | Task 12 |

All spec requirements covered. ✓

**Placeholder scan:** No TBD, TODO, or "similar to Task N" references. Every step contains complete
code. ✓

**Type consistency:**

- `MessageDef.type` typed as `MessageType` throughout Tasks 9 and 10. ✓
- `validateAllExamples` signature matches between Task 9 (definition) and Task 10 (call site). ✓
- `families` and `sharedTypes` exported from `protocol.ts` consumed in `+page.server.ts`. ✓
- `PageServerData` from `./$types` used in both `+page.svelte` files. ✓
- `HandshakeMessageSchema` imported from `./handshake` (value import added in Task 3 Step 2). ✓

**Corrections vs. spec:**

- `Type.Strict()` absent everywhere — correct for TypeBox 1.x. ✓
- `+page.server.ts` used for both Shiki routes — Shiki stays out of client bundle. ✓
- No `optimizeDeps.exclude` in `vite.config.ts` — Vite handles linked ESM deps automatically. ✓
- `command_call.args` uses `JsonObjectSchema` — matches C# JObject expectation. ✓
- `assembly_reload` schema and family entry present. ✓
- Exhaustiveness check is a runtime Set assertion, not the broken type trick. ✓
