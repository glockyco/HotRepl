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
        onChange: true,
        timeoutMs: 250,
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
