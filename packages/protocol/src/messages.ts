import type { ErrorKind } from "./error-kinds";
import type { HandshakeMessage } from "./handshake";
import type { MESSAGE_TYPES } from "./message-types";

export type JsonObject = Record<string, unknown>;

export interface HotReplErrorEnvelope {
  kind: ErrorKind;
  code: string;
  message: string;
  retryable: boolean;
  details?: unknown;
}

export interface ArtifactRef {
  uri: string;
  path?: string;
  sha256: string;
  byteSize: number;
  contentType: string;
  finalized: boolean;
}

export interface CommandSummary {
  name: string;
  majorVersion: number;
  kind: "sync" | "job";
  mutatesState: boolean;
}

export interface CommandDescriptor extends CommandSummary {
  inputSchema: JsonObject;
  outputSchema: JsonObject;
  artifactsSchema: JsonObject;
  cancellation?: string;
}

export interface JournalEntry {
  id: string;
  kind: "eval" | "command";
  name?: string;
  code?: string;
  success: boolean;
  durationMs: number;
  errorKind?: ErrorKind;
  timestamp: string;
}

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
  | CommandsListResultMessage
  | CommandDescribeResultMessage
  | CommandResultMessage
  | JobAcceptedMessage
  | JobStatusResultMessage
  | JobResultMessage
  | JobCancelResultMessage
  | JournalQueryResultMessage;
