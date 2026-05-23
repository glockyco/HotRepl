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
