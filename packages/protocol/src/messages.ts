import { type Static, Type } from "typebox";
import { Value } from "typebox/value";
import type { ErrorKind } from "./error-kinds";
import { ERROR_KINDS } from "./error-kinds";
import { type HandshakeMessage, HandshakeMessageSchema } from "./handshake";
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
