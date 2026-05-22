import {
  type ArtifactRef,
  type CommandDescriptor,
  defaultLimits,
  ERROR_KINDS,
  type EvalErrorMessage,
  type EvalResultMessage,
  type HandshakeMessage,
  type HotReplErrorEnvelope,
  type JobResultMessage,
  type JournalEntry,
  MESSAGE_TYPES,
  PROTOCOL_VERSION,
  type RuntimeLimits,
  type ServerMessage,
  type SessionEvictedMessage,
  type SubscribeErrorMessage,
  type SubscribeResultMessage,
} from "@hotrepl/protocol";
import { HotReplError, type RuntimeRequest, type RuntimeTransport, sha256Hex } from "@hotrepl/sdk";

type CommandOutput = {
  output?: unknown;
  artifacts?: Record<string, ArtifactRef>;
};

type CommandHandler = (args: Record<string, any>) => Promise<CommandOutput> | CommandOutput;

type RegisteredCommand = {
  descriptor: CommandDescriptor;
  handler: CommandHandler;
  completeAfterPolls: number;
};

type Job = {
  id: string;
  requestId: string;
  command: RegisteredCommand;
  args: Record<string, any>;
  pollsRemaining: number;
  result?: CommandOutput;
  error?: HotReplErrorEnvelope;
  state: "running" | "done" | "failed" | "cancelled";
};

type WatchEvent =
  | { hasValue?: boolean; value?: unknown; valueType?: string; final: boolean }
  | { error: HotReplErrorEnvelope; final: boolean };

export interface FakeRuntimeOptions {
  protocolVersion?: number;
  supportsCompletion?: boolean;
  limits?: Partial<RuntimeLimits>;
}

export interface RegisterCommandOptions {
  completeAfterPolls?: number;
}

export class FakeRuntime implements RuntimeTransport {
  private readonly commands = new Map<string, RegisteredCommand>();
  private readonly jobs = new Map<string, Job>();
  private readonly artifacts = new Map<string, Uint8Array>();
  private readonly completions = new Map<string, string[]>();
  private readonly watches = new Map<string, WatchEvent[]>();
  private readonly journalEntries: JournalEntry[] = [];
  private readonly counters = new Map<string, number>();
  private readonly evictionListeners = new Set<(event: SessionEvictedMessage) => void>();
  private evalHandler: (code: string) => { value?: unknown; valueType?: string } = () => ({});
  private activeRequests = 0;
  private nextJob = 0;

  readonly handshakeMessage: HandshakeMessage;

  constructor(options: FakeRuntimeOptions = {}) {
    this.handshakeMessage = {
      type: MESSAGE_TYPES.handshake,
      protocolVersion: (options.protocolVersion ?? PROTOCOL_VERSION) as 2,
      host: { name: "FakeRuntime", version: "0.0.0", platform: "test" },
      evaluator: {
        name: "FakeEvaluator",
        languageVersion: "test",
        persistentState: true,
        supportsCompletion: options.supportsCompletion ?? false,
        cancellation: "cooperative",
      },
      availableEvaluators: ["FakeEvaluator"],
      defaultUsings: [],
      helpers: [],
      control: { supported: true, commandsListChanged: false, schemaValidation: false },
      limits: { ...defaultLimits, ...options.limits },
      enforces: [
        "maxMessageBytes",
        "maxQueuedCommands",
        "maxResultLength",
        "maxEnumerableElements",
        "maxJobConcurrency",
      ],
    };
  }

  async handshake(): Promise<HandshakeMessage> {
    return this.handshakeMessage;
  }

  registerCommand(
    descriptor: CommandDescriptor,
    handler: CommandHandler,
    options: RegisterCommandOptions = {},
  ): void {
    this.commands.set(descriptor.name, {
      descriptor,
      handler,
      completeAfterPolls: options.completeAfterPolls ?? 2,
    });
  }

  setEvalHandler(handler: (code: string) => { value?: unknown; valueType?: string }): void {
    this.evalHandler = handler;
  }

  registerCompletion(prefix: string, completions: string[]): void {
    this.completions.set(prefix, completions);
  }

  setWatch(code: string, events: WatchEvent[]): void {
    this.watches.set(code, events);
  }

  errorKinds(): readonly string[] {
    return ERROR_KINDS;
  }

  requestCount(type: RuntimeRequest["type"], name?: string): number {
    return this.counters.get(counterKey(type, name)) ?? 0;
  }

  async request(request: RuntimeRequest): Promise<ServerMessage> {
    this.rejectQueueFull();
    this.activeRequests += 1;
    try {
      this.count(request);
      this.rejectOversized(request);

      switch (request.type) {
        case "eval":
          return this.handleEval(request);
        case "complete":
          return this.handleComplete(request);
        case "reset":
          return { type: MESSAGE_TYPES.resetResult, id: request.id, success: true };
        case "commands_list":
          return {
            type: MESSAGE_TYPES.commandsListResult,
            id: request.id,
            commands: [...this.commands.values()].map((command) => command.descriptor),
          };
        case "command_describe":
          return this.handleDescribe(request);
        case "command_call":
          return await this.handleCommandCall(request);
        case "job_status":
          return await this.handleJobStatus(request);
        case "job_cancel":
          return this.handleJobCancel(request);
        case "journal_query":
          return this.handleJournalQuery(request);
        case "subscribe":
          throw error("invalid_request", "useWatch", "Use watch() for subscriptions.");
      }
    } finally {
      this.activeRequests -= 1;
    }
  }

  async *watch(
    request: Extract<RuntimeRequest, { type: "subscribe" }>,
  ): AsyncIterable<SubscribeResultMessage | SubscribeErrorMessage> {
    this.count(request);
    this.rejectOversized(request);
    const events = this.watches.get(request.code) ?? [];
    let seq = 0;
    for (const event of events) {
      seq += 1;
      if ("error" in event) {
        yield {
          type: MESSAGE_TYPES.subscribeError,
          id: request.id,
          seq,
          error: event.error,
          final: event.final,
        };
      } else {
        const hasValue = event.hasValue ?? event.value !== undefined;
        const response: SubscribeResultMessage = {
          type: MESSAGE_TYPES.subscribeResult,
          id: request.id,
          seq,
          hasValue,
          durationMs: 0,
          final: event.final,
        };
        if (hasValue && event.value !== undefined) response.value = event.value;
        if (event.valueType !== undefined) response.valueType = event.valueType;
        yield response;
      }
    }
  }

  async readArtifact(ref: ArtifactRef): Promise<Uint8Array> {
    const bytes = this.artifacts.get(ref.uri);
    if (bytes === undefined) {
      throw error("artifact_missing", "artifactMissing", `Artifact ${ref.uri} is missing.`);
    }
    return bytes;
  }

  onSessionEvicted(listener: (event: SessionEvictedMessage) => void): () => void {
    this.evictionListeners.add(listener);
    return () => this.evictionListeners.delete(listener);
  }

  evict(reason: string): void {
    const event: SessionEvictedMessage = { type: MESSAGE_TYPES.sessionEvicted, reason };
    for (const listener of this.evictionListeners) listener(event);
  }

  async putArtifact(
    name: string,
    bytes: Uint8Array,
    options: { contentType?: string; path?: string } = {},
  ): Promise<ArtifactRef> {
    const uri = `hotrepl://artifact/${name}`;
    this.artifacts.set(uri, bytes);
    const ref: ArtifactRef = {
      uri,
      sha256: await sha256Hex(bytes),
      byteSize: bytes.byteLength,
      contentType: options.contentType ?? "application/octet-stream",
      finalized: true,
    };
    if (options.path !== undefined) ref.path = options.path;
    return ref;
  }

  overwriteArtifact(uri: string, bytes: Uint8Array): void {
    this.artifacts.set(uri, bytes);
  }

  private handleEval(
    request: Extract<RuntimeRequest, { type: "eval" }>,
  ): EvalResultMessage | EvalErrorMessage {
    try {
      const result = this.evalHandler(request.code);
      const value = this.limitOutput(result.value);
      this.record({ id: request.id, kind: "eval", success: true });
      const response: EvalResultMessage = {
        type: MESSAGE_TYPES.evalResult,
        id: request.id,
        hasValue: value !== undefined,
        value,
        durationMs: 0,
      };
      if (result.valueType !== undefined) response.valueType = result.valueType;
      return response;
    } catch (caught) {
      const envelope = toEnvelope(caught);
      this.record({ id: request.id, kind: "eval", success: false, errorKind: envelope.kind });
      return { type: MESSAGE_TYPES.evalError, id: request.id, error: envelope };
    }
  }

  private handleComplete(
    request: Extract<RuntimeRequest, { type: "complete" }>,
  ): ServerMessage {
    return {
      type: MESSAGE_TYPES.completeResult,
      id: request.id,
      completions: this.completions.get(request.code) ?? [],
      durationMs: 0,
    };
  }

  private handleDescribe(
    request: Extract<RuntimeRequest, { type: "command_describe" }>,
  ): ServerMessage {
    const command = this.commands.get(request.name);
    if (command === undefined) {
      throw error("unknown_command", "unknownCommand", `Unknown command ${request.name}.`);
    }
    return {
      type: MESSAGE_TYPES.commandDescribeResult,
      id: request.id,
      descriptor: command.descriptor,
    };
  }

  private async handleCommandCall(
    request: Extract<RuntimeRequest, { type: "command_call" }>,
  ): Promise<ServerMessage> {
    const command = this.commands.get(request.name);
    if (command === undefined) {
      return failedCommand(
        request.id,
        errorEnvelope("unknown_command", "unknownCommand", "Unknown command."),
      );
    }
    if (command.descriptor.kind === "job") {
      if (this.runningJobCount() >= this.handshakeMessage.limits.maxJobConcurrency) {
        return failedCommand(
          request.id,
          errorEnvelope(
            "busy",
            "jobConcurrencyLimit",
            "Maximum concurrent command job limit reached.",
            true,
          ),
        );
      }
      const jobId = `job-${++this.nextJob}`;
      this.jobs.set(jobId, {
        id: jobId,
        requestId: request.id,
        command,
        args: request.args as Record<string, any>,
        pollsRemaining: command.completeAfterPolls,
        state: "running",
      });
      return { type: MESSAGE_TYPES.jobAccepted, id: request.id, jobId, state: "running" };
    }

    try {
      const result = await command.handler(request.args as Record<string, any>);
      const output = this.limitOutput(result.output);
      this.record({ id: request.id, kind: "command", name: request.name, success: true });
      return {
        type: MESSAGE_TYPES.commandResult,
        id: request.id,
        status: "ok",
        output,
        artifacts: result.artifacts ?? {},
        durationMs: 0,
      };
    } catch (caught) {
      const envelope = toEnvelope(caught);
      this.record({
        id: request.id,
        kind: "command",
        name: request.name,
        success: false,
        errorKind: envelope.kind,
      });
      return failedCommand(request.id, envelope);
    }
  }

  private async handleJobStatus(
    request: Extract<RuntimeRequest, { type: "job_status" }>,
  ): Promise<ServerMessage> {
    const job = this.jobs.get(request.jobId);
    if (job === undefined) throw error("artifact_missing", "jobMissing", "Job is missing.");
    if (job.state !== "running") {
      return this.jobResult(request.id, job);
    }
    if (job.pollsRemaining > 1) {
      job.pollsRemaining -= 1;
      return {
        type: MESSAGE_TYPES.jobStatusResult,
        id: request.id,
        jobId: job.id,
        state: "running",
      };
    }
    await this.finishJob(job);
    return this.jobResult(request.id, job);
  }

  private handleJobCancel(
    request: Extract<RuntimeRequest, { type: "job_cancel" }>,
  ): ServerMessage {
    const job = this.jobs.get(request.jobId);
    if (job !== undefined && job.state === "running") job.state = "cancelled";
    return {
      type: MESSAGE_TYPES.jobCancelResult,
      id: request.id,
      accepted: job !== undefined,
      state: job?.state ?? "cancelled",
    };
  }

  private handleJournalQuery(
    request: Extract<RuntimeRequest, { type: "journal_query" }>,
  ): ServerMessage {
    let entries = this.journalEntries;
    if (request.kind !== undefined) {
      entries = entries.filter((entry) => entry.kind === request.kind);
    }
    if (request.limit !== undefined) entries = entries.slice(-request.limit);
    return { type: MESSAGE_TYPES.journalQueryResult, id: request.id, entries };
  }

  private async finishJob(job: Job): Promise<void> {
    try {
      const result = await job.command.handler(job.args);
      const output: CommandOutput = { output: this.limitOutput(result.output) };
      if (result.artifacts !== undefined) output.artifacts = result.artifacts;
      job.result = output;
      job.state = "done";
      this.record({
        id: job.requestId,
        kind: "command",
        name: job.command.descriptor.name,
        success: true,
      });
    } catch (caught) {
      job.error = toEnvelope(caught);
      job.state = "failed";
      this.record({
        id: job.requestId,
        kind: "command",
        name: job.command.descriptor.name,
        success: false,
        errorKind: job.error.kind,
      });
    }
  }

  private jobResult(id: string, job: Job): JobResultMessage {
    if (job.state === "done") {
      return {
        type: MESSAGE_TYPES.jobResult,
        id,
        jobId: job.id,
        state: "done",
        status: "ok",
        output: job.result?.output,
        artifacts: job.result?.artifacts ?? {},
        durationMs: 0,
      };
    }
    return {
      type: MESSAGE_TYPES.jobResult,
      id,
      jobId: job.id,
      state: job.state === "cancelled" ? "cancelled" : "failed",
      status: "failed",
      error: job.error ?? errorEnvelope("cancelled", "jobCancelled", "Job cancelled."),
      artifacts: {},
      durationMs: 0,
    };
  }

  private rejectQueueFull(): void {
    if (this.activeRequests < this.handshakeMessage.limits.maxQueuedCommands) {
      return;
    }

    throw error("busy", "commandQueueFull", "Command queue is full.", true);
  }

  private rejectOversized(request: RuntimeRequest): void {
    if (
      new TextEncoder().encode(JSON.stringify(request)).byteLength
        <= this.handshakeMessage.limits.maxMessageBytes
    ) {
      return;
    }
    throw error("invalid_request", "messageTooLarge", "Message exceeds maxMessageBytes.");
  }

  private limitOutput(output: unknown): unknown {
    const capped = Array.isArray(output)
      ? output.slice(0, this.handshakeMessage.limits.maxEnumerableElements)
      : output;
    const serialized = JSON.stringify(capped);
    if (
      serialized !== undefined
      && new TextEncoder().encode(serialized).byteLength
        > this.handshakeMessage.limits.maxResultLength
    ) {
      throw error("internal", "resultTooLarge", "Result exceeds maxResultLength.");
    }

    return capped;
  }

  private runningJobCount(): number {
    let count = 0;
    for (const job of this.jobs.values()) {
      if (job.state === "running") count += 1;
    }
    return count;
  }

  private count(request: RuntimeRequest): void {
    const name = request.type === "command_describe" || request.type === "command_call"
      ? request.name
      : undefined;
    const keys = new Set([counterKey(request.type), counterKey(request.type, name)]);
    for (const key of keys) this.counters.set(key, (this.counters.get(key) ?? 0) + 1);
  }

  private record(entry: {
    id: string;
    kind: "eval" | "command";
    name?: string;
    success: boolean;
    errorKind?: HotReplErrorEnvelope["kind"];
  }): void {
    const journalEntry: JournalEntry = {
      id: entry.id,
      kind: entry.kind,
      success: entry.success,
      durationMs: 0,
      timestamp: new Date(0).toISOString(),
    };
    if (entry.name !== undefined) journalEntry.name = entry.name;
    if (entry.errorKind !== undefined) journalEntry.errorKind = entry.errorKind;
    this.journalEntries.push(journalEntry);
  }
}

function counterKey(type: string, name?: string): string {
  return name === undefined ? type : `${type}:${name}`;
}

function error(
  kind: HotReplErrorEnvelope["kind"],
  code: string,
  message: string,
  retryable = false,
): HotReplError {
  return new HotReplError({ kind, code, message, retryable });
}

function errorEnvelope(
  kind: HotReplErrorEnvelope["kind"],
  code: string,
  message: string,
  retryable = false,
): HotReplErrorEnvelope {
  return { kind, code, message, retryable };
}

function failedCommand(id: string, envelope: HotReplErrorEnvelope): ServerMessage {
  return {
    type: MESSAGE_TYPES.commandResult,
    id,
    status: "failed",
    error: envelope,
    artifacts: {},
    durationMs: 0,
  };
}

function toEnvelope(caught: unknown): HotReplErrorEnvelope {
  if (caught instanceof HotReplError) {
    return {
      kind: caught.kind,
      code: caught.code,
      message: caught.message,
      retryable: caught.retryable,
      details: caught.details,
    };
  }
  return errorEnvelope(
    "internal",
    "handlerException",
    caught instanceof Error ? caught.message : String(caught),
  );
}
