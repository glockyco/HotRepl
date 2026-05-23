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

// ── Server-sent messages (unchanged interfaces — will be replaced in next step) ──

export interface EvalResultMessage {
  type: typeof MESSAGE_TYPES.evalResult;
  id: string;
  hasValue: boolean;
  value?: unknown;
  valueType?: string;
  stdout?: string;
  durationMs: number;
}

export interface EvalErrorMessage {
  type: typeof MESSAGE_TYPES.evalError;
  id: string;
  error: HotReplErrorEnvelope;
}

export interface CompleteResultMessage {
  type: typeof MESSAGE_TYPES.completeResult;
  id: string;
  completions: string[];
  durationMs: number;
}

export interface ResetResultMessage {
  type: typeof MESSAGE_TYPES.resetResult;
  id: string;
  success: boolean;
}

export interface SubscribeResultMessage {
  type: typeof MESSAGE_TYPES.subscribeResult;
  id: string;
  seq: number;
  hasValue: boolean;
  value?: unknown;
  valueType?: string;
  durationMs: number;
  final: boolean;
}

export interface SubscribeErrorMessage {
  type: typeof MESSAGE_TYPES.subscribeError;
  id: string;
  seq: number;
  error: HotReplErrorEnvelope;
  final: boolean;
}

export interface SessionEvictedMessage {
  type: typeof MESSAGE_TYPES.sessionEvicted;
  reason: string;
  by?: { clientName?: string };
}

export interface ProtocolErrorMessage {
  type: typeof MESSAGE_TYPES.error;
  id?: string;
  error: HotReplErrorEnvelope;
}

export interface CommandsListResultMessage {
  type: typeof MESSAGE_TYPES.commandsListResult;
  id: string;
  commands: CommandSummary[];
  since?: string;
}

export interface CommandDescribeResultMessage {
  type: typeof MESSAGE_TYPES.commandDescribeResult;
  id: string;
  descriptor: CommandDescriptor;
}

export interface CommandResultMessage {
  type: typeof MESSAGE_TYPES.commandResult;
  id: string;
  status: "ok" | "failed";
  output?: unknown;
  artifacts: Record<string, ArtifactRef>;
  error?: HotReplErrorEnvelope;
  durationMs: number;
}

export interface JobAcceptedMessage {
  type: typeof MESSAGE_TYPES.jobAccepted;
  id: string;
  jobId: string;
  state: "running";
}

export interface JobStatusResultMessage {
  type: typeof MESSAGE_TYPES.jobStatusResult;
  id: string;
  jobId: string;
  state: "running";
  progress?: unknown;
  error?: HotReplErrorEnvelope;
}

export interface JobResultMessage {
  type: typeof MESSAGE_TYPES.jobResult;
  id: string;
  jobId: string;
  state: "done" | "failed" | "cancelled";
  status: "ok" | "failed";
  output?: unknown;
  artifacts: Record<string, ArtifactRef>;
  error?: HotReplErrorEnvelope;
  durationMs: number;
}

export interface JobCancelResultMessage {
  type: typeof MESSAGE_TYPES.jobCancelResult;
  id: string;
  accepted: boolean;
  state: "running" | "done" | "failed" | "cancelled";
}

export interface JournalQueryResultMessage {
  type: typeof MESSAGE_TYPES.journalQueryResult;
  id: string;
  entries: JournalEntry[];
}

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
  | JournalQueryResultMessage;
