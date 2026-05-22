import { ERROR_KINDS, MESSAGE_TYPES, PROTOCOL_VERSION } from "@hotrepl/protocol";
import { describe, expect, test } from "bun:test";
import { FakeRuntime } from "../src";

describe("FakeRuntime", () => {
  test("returns an honest v2 handshake", async () => {
    const runtime = new FakeRuntime({ supportsCompletion: true });

    const handshake = await runtime.handshake();

    expect(handshake.type).toBe(MESSAGE_TYPES.handshake);
    expect(handshake.protocolVersion).toBe(PROTOCOL_VERSION);
    expect(handshake.evaluator.supportsCompletion).toBe(true);
    expect(handshake.enforces).toContain("maxMessageBytes");
    expect(handshake.limits.maxJobConcurrency).toBe(1);
  });

  test("applies maxMessageBytes before handling a frame", async () => {
    const runtime = new FakeRuntime({ limits: { maxMessageBytes: 16 } });

    await expect(
      runtime.request({ type: "eval", id: "eval-1", code: "1".repeat(64) }),
    ).rejects.toMatchObject({ kind: "invalid_request", code: "messageTooLarge" });
  });

  test("applies maxMessageBytes to subscriptions before yielding events", async () => {
    const runtime = new FakeRuntime({ limits: { maxMessageBytes: 16 } });
    const iterator = runtime
      .watch({ type: "subscribe", id: "watch-1", code: "1".repeat(64) })
      [Symbol.asyncIterator]();

    await expect(iterator.next()).rejects.toMatchObject({
      kind: "invalid_request",
      code: "messageTooLarge",
    });
  });
  test("returns eval_error frames when eval handlers fail", async () => {
    const runtime = new FakeRuntime();
    runtime.setEvalHandler(() => {
      throw new Error("boom");
    });

    const response = await runtime.request({ type: "eval", id: "eval-1", code: "throw" });

    expect(response.type).toBe(MESSAGE_TYPES.evalError);
    if (response.type !== MESSAGE_TYPES.evalError) {
      throw new Error(`Expected eval_error, got ${response.type}.`);
    }
    expect(response.error).toMatchObject({ kind: "internal", code: "handlerException" });
  });

  test("rejects queued requests and concurrent jobs beyond advertised limits", async () => {
    const runtime = new FakeRuntime({ limits: { maxQueuedCommands: 1, maxJobConcurrency: 1 } });
    let releaseFirstCommand: (() => void) | undefined;
    runtime.registerCommand(
      {
        name: "slow.sync",
        majorVersion: 1,
        kind: "sync",
        mutatesState: false,
        inputSchema: { type: "object" },
        outputSchema: { type: "object" },
        artifactsSchema: { type: "object" },
      },
      () =>
        new Promise((resolve) => {
          releaseFirstCommand = () => resolve({ output: { ok: true } });
        }),
    );
    runtime.registerCommand(
      {
        name: "slow.job",
        majorVersion: 1,
        kind: "job",
        mutatesState: false,
        inputSchema: { type: "object" },
        outputSchema: { type: "object" },
        artifactsSchema: { type: "object" },
      },
      () => ({ output: { ok: true } }),
      { completeAfterPolls: 2 },
    );

    const firstCommand = runtime.request({
      type: "command_call",
      id: "sync-1",
      name: "slow.sync",
      args: {},
    });
    await Promise.resolve();

    await expect(
      runtime.request({ type: "command_call", id: "sync-2", name: "slow.sync", args: {} }),
    ).rejects.toMatchObject({ kind: "busy", code: "commandQueueFull" });
    releaseFirstCommand?.();
    await firstCommand;

    const firstJob = await runtime.request({
      type: "command_call",
      id: "job-1",
      name: "slow.job",
      args: {},
    });
    expect(firstJob.type).toBe(MESSAGE_TYPES.jobAccepted);
    if (firstJob.type !== MESSAGE_TYPES.jobAccepted) {
      throw new Error(`Expected job_accepted, got ${firstJob.type}.`);
    }

    const rejectedJob = await runtime.request({
      type: "command_call",
      id: "job-2",
      name: "slow.job",
      args: {},
    });
    expect(rejectedJob.type).toBe(MESSAGE_TYPES.commandResult);
    if (rejectedJob.type !== MESSAGE_TYPES.commandResult) {
      throw new Error(`Expected command_result, got ${rejectedJob.type}.`);
    }
    expect(rejectedJob.status).toBe("failed");
    expect(rejectedJob.error).toMatchObject({ kind: "busy", code: "jobConcurrencyLimit" });

    const cancelled = await runtime.request({
      type: "job_cancel",
      id: "cancel-1",
      jobId: firstJob.jobId,
    });
    expect(cancelled.type).toBe(MESSAGE_TYPES.jobCancelResult);

    const cancelledStatus = await runtime.request({
      type: "job_status",
      id: "status-1",
      jobId: firstJob.jobId,
    });
    expect(cancelledStatus.type).toBe(MESSAGE_TYPES.jobResult);
    if (cancelledStatus.type !== MESSAGE_TYPES.jobResult) {
      throw new Error(`Expected job_result, got ${cancelledStatus.type}.`);
    }
    expect(cancelledStatus.state).toBe("cancelled");
  });

  test("caps enumerable outputs and rejects oversized results", async () => {
    const runtime = new FakeRuntime({ limits: { maxEnumerableElements: 2, maxResultLength: 16 } });
    runtime.setEvalHandler(() => ({ value: [1, 2, 3] }));
    runtime.registerCommand(
      {
        name: "large.sync",
        majorVersion: 1,
        kind: "sync",
        mutatesState: false,
        inputSchema: { type: "object" },
        outputSchema: { type: "object" },
        artifactsSchema: { type: "object" },
      },
      () => ({ output: "x".repeat(64) }),
    );

    const evalResponse = await runtime.request({ type: "eval", id: "eval-1", code: "range" });
    expect(evalResponse.type).toBe(MESSAGE_TYPES.evalResult);
    if (evalResponse.type !== MESSAGE_TYPES.evalResult) {
      throw new Error(`Expected eval_result, got ${evalResponse.type}.`);
    }
    expect(evalResponse.value).toEqual([1, 2]);

    const commandResponse = await runtime.request({
      type: "command_call",
      id: "cmd-1",
      name: "large.sync",
      args: {},
    });
    expect(commandResponse.type).toBe(MESSAGE_TYPES.commandResult);
    if (commandResponse.type !== MESSAGE_TYPES.commandResult) {
      throw new Error(`Expected command_result, got ${commandResponse.type}.`);
    }
    expect(commandResponse.status).toBe("failed");
    expect(commandResponse.error).toMatchObject({ kind: "internal", code: "resultTooLarge" });
  });

  test("stores commands, artifacts, jobs, and journal entries", async () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(
      {
        name: "data.export",
        majorVersion: 1,
        kind: "job",
        mutatesState: false,
        inputSchema: { type: "object" },
        outputSchema: { type: "object" },
        artifactsSchema: { type: "object" },
      },
      async () => ({ output: { done: true } }),
      { completeAfterPolls: 2 },
    );

    const accepted = await runtime.request({
      type: "command_call",
      id: "cmd-1",
      name: "data.export",
      args: {},
    });
    expect(accepted.type).toBe(MESSAGE_TYPES.jobAccepted);
    if (accepted.type !== MESSAGE_TYPES.jobAccepted) {
      throw new Error(`Expected job_accepted, got ${accepted.type}.`);
    }

    const running = await runtime.request({
      type: "job_status",
      id: "status-1",
      jobId: accepted.jobId,
    });
    expect(running.type).toBe(MESSAGE_TYPES.jobStatusResult);
    if (running.type !== MESSAGE_TYPES.jobStatusResult) {
      throw new Error(`Expected job_status_result, got ${running.type}.`);
    }
    expect(running.state).toBe("running");

    const done = await runtime.request({
      type: "job_status",
      id: "status-2",
      jobId: accepted.jobId,
    });
    expect(done.type).toBe(MESSAGE_TYPES.jobResult);
    if (done.type !== MESSAGE_TYPES.jobResult) {
      throw new Error(`Expected job_result, got ${done.type}.`);
    }
    expect(done.output).toEqual({ done: true });

    const journal = await runtime.request({ type: "journal_query", id: "journal-1" });
    if (journal.type !== MESSAGE_TYPES.journalQueryResult) {
      throw new Error(`Expected journal_query_result, got ${journal.type}.`);
    }
    expect(journal.entries.some((entry) => entry.kind === "command")).toBe(true);
  });

  test("uses the closed error kind vocabulary", () => {
    const runtime = new FakeRuntime();

    expect(runtime.errorKinds()).toEqual(ERROR_KINDS);
  });
});
