import { type Static, Type } from "typebox";
import type { TLiteral } from "typebox";
import type { ErrorKind } from "./error-kinds";
import { ERROR_KINDS } from "./error-kinds";
import { type HandshakeMessage, HandshakeMessageSchema } from "./handshake";
import { MESSAGE_TYPES } from "./message-types";
// Preserve literal tuple types for Type.Union — plain .map() loses tuple info in TypeBox 1.x
type LiteralTuple<T extends readonly string[]> = T extends readonly [
  infer Head extends string,
  ...infer Tail extends readonly string[],
] ? [TLiteral<Head>, ...LiteralTuple<Tail>]
  : [];
type ErrorKindLiteralTuple = LiteralTuple<typeof ERROR_KINDS>;
const ERROR_KIND_LITERALS = ERROR_KINDS.map((k) =>
  Type.Literal(k)
) as unknown as ErrorKindLiteralTuple;

export type JsonObject = Record<string, unknown>;

// ── Shared types ──────────────────────────────────────────────────────────────

/** Opaque JSON object — used for command args and inline JSON schemas */
export const JsonObjectSchema = Type.Record(Type.String(), Type.Unknown());

/** Unified error envelope used in every failure response */
export const ErrorEnvelopeSchema = Type.Object(
  {
    kind: Type.Union(ERROR_KIND_LITERALS),
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
    errorKind: Type.Optional(Type.Union(ERROR_KIND_LITERALS)),
    timestamp: Type.String(),
  },
  { additionalProperties: false },
);
export type JournalEntry = Static<typeof JournalEntrySchema>;

// ── Server-sent messages ──────────────────────────────────────────────────────

export const EvalResultMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.evalResult),
    id: Type.String(),
    hasValue: Type.Boolean(),
    value: Type.Optional(Type.Unknown()),
    valueType: Type.Optional(Type.String()),
    truncated: Type.Optional(Type.Boolean()),
    truncatedBytes: Type.Optional(Type.Number()),
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
    truncated: Type.Optional(Type.Boolean()),
    truncatedBytes: Type.Optional(Type.Number()),
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
    jobId: Type.String(),
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

/** Sent by the server when a game assembly is hot-reloaded. The SDK surfaces this via
 *  Session.onAssemblyReload and invalidates its cached command catalog/descriptors. */
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
    onChange: Type.Optional(Type.Boolean()),
    limit: Type.Optional(Type.Number()),
    timeoutMs: Type.Optional(Type.Number()),
  },
  { additionalProperties: false },
);
export type SubscribeMessage = Static<typeof SubscribeMessageSchema>;

/** Cancels an active eval or subscription by its request id. Sent by the SDK via
 *  Session.cancel(targetId), and automatically when a watch() iterator stops early. */
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
