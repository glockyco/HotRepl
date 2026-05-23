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

// ── Runtime exhaustiveness assertion ──────────────────────────────────────────────────────────
// Runs at module load time (= prerender time). Build fails with a clear message
// if any MESSAGE_TYPES discriminant is absent from the families array.
export function assertExhaustive(f: MessageFamily[]): void {
  const documented = new Set<string>(f.flatMap((fam) => fam.messages.map((m) => m.type)));
  const missing = (Object.values(MESSAGE_TYPES) as string[]).filter((t) => !documented.has(t));
  if (missing.length > 0) {
    throw new Error(
      `Protocol reference is missing documentation for: ${missing.join(", ")}. `
        + `Add entries to site/src/lib/data/protocol.ts.`,
    );
  }
}

// ── Validation helper ────────────────────────────────────────────────────────────────
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

// ── Data ─────────────────────────────────────────────────────────────────────────────

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
