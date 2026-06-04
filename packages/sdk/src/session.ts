import { MESSAGE_TYPES, PROTOCOL_VERSION } from "@hotrepl/protocol";
import type {
  ArtifactRef,
  CommandDescribeResultMessage,
  CommandDescriptor,
  CommandResultMessage,
  CommandsListResultMessage,
  CommandSummary,
  CompleteResultMessage,
  EvalErrorMessage,
  EvalResultMessage,
  HandshakeMessage,
  HotReplErrorEnvelope,
  JobAcceptedMessage,
  JobCancelResultMessage,
  JobResultMessage,
  JobStatusResultMessage,
  JournalEntry,
  JournalQueryResultMessage,
  ResetResultMessage,
  ServerMessage,
  SessionEvictedMessage,
  SubscribeErrorMessage,
  SubscribeResultMessage,
} from "@hotrepl/protocol";
import { Artifact } from "./artifact";
import { type DescriptorCache, type Result, toResult } from "./commands";
import { HotReplError, HotReplSessionEvicted } from "./errors";

export type RuntimeRequest =
  | { type: "eval"; id: string; code: string; timeoutMs?: number }
  | { type: "complete"; id: string; code: string; cursor?: number }
  | { type: "reset"; id: string }
  | { type: "subscribe"; id: string; code: string; intervalFrames?: number; limit?: number }
  | { type: "commands_list"; id: string; since?: string }
  | { type: "command_describe"; id: string; name: string }
  | { type: "command_call"; id: string; name: string; args: unknown; timeoutMs?: number }
  | { type: "job_status"; id: string; jobId: string }
  | { type: "job_cancel"; id: string; jobId: string }
  | { type: "journal_query"; id: string; kind?: "eval" | "command"; limit?: number };

export interface RuntimeTransport {
  handshake(): Promise<HandshakeMessage>;
  request(request: RuntimeRequest): Promise<ServerMessage>;
  watch(request: Extract<RuntimeRequest, { type: "subscribe" }>): AsyncIterable<WatchWireMessage>;
  readArtifact(ref: ArtifactRef): Promise<Uint8Array>;
  onSessionEvicted(listener: (event: SessionEvictedMessage) => void): () => void;
  close(): void;
}

export type WatchWireMessage = SubscribeResultMessage | SubscribeErrorMessage;

export interface RunOptions {
  timeoutMs?: number;
  pollIntervalMs?: number;
  wait?: boolean;
}

export interface EvalResponse<T = unknown> {
  hasValue: boolean;
  value?: T;
  valueType?: string;
  truncated?: boolean;
  truncatedBytes?: number;
  stdout?: string;
  durationMs: number;
}

export interface JobStatus {
  jobId: string;
  state: "running" | "done" | "failed" | "cancelled";
  progress?: unknown;
}

export interface WatchTick<T = unknown> {
  seq: number;
  hasValue: boolean;
  value?: T;
  valueType?: string;
  truncated?: boolean;
  truncatedBytes?: number;
  final: boolean;
  durationMs: number;
}

export class JobHandle<T = unknown> {
  readonly jobId: string;
  private readonly session: Session;
  private readonly pollIntervalMs: number;

  constructor(session: Session, jobId: string, pollIntervalMs: number) {
    this.session = session;
    this.jobId = jobId;
    this.pollIntervalMs = pollIntervalMs;
  }

  async status(): Promise<JobStatus> {
    const response = await this.session.request<JobStatusResultMessage | JobResultMessage>({
      type: "job_status",
      id: this.session.nextId("status"),
      jobId: this.jobId,
    });
    if (response.type === MESSAGE_TYPES.jobResult) {
      return { jobId: response.jobId, state: response.state };
    }
    return { jobId: response.jobId, state: response.state, progress: response.progress };
  }

  async result<TOutput = T>(): Promise<Result<TOutput>> {
    return this.session.pollJob<TOutput>(this.jobId, this.pollIntervalMs);
  }

  async cancel(): Promise<JobCancelResultMessage> {
    return this.session.request<JobCancelResultMessage>({
      type: "job_cancel",
      id: this.session.nextId("cancel"),
      jobId: this.jobId,
    });
  }
}

export class Session {
  readonly handshake: HandshakeMessage;
  private readonly transport: RuntimeTransport;
  private readonly descriptors: DescriptorCache = new Map();
  private catalog: CommandSummary[] | undefined;
  private readonly evictionListeners = new Set<(event: SessionEvictedMessage) => void>();
  private sequence = 0;
  private evicted: SessionEvictedMessage | undefined;
  private closed = false;

  constructor(transport: RuntimeTransport, handshake: HandshakeMessage) {
    this.transport = transport;
    this.handshake = handshake;
    this.transport.onSessionEvicted((event) => {
      this.evicted = event;
      for (const listener of this.evictionListeners) listener(event);
    });
  }

  onSessionEvicted(listener: (event: SessionEvictedMessage) => void): () => void {
    this.evictionListeners.add(listener);
    return () => this.evictionListeners.delete(listener);
  }

  async run<T = unknown>(
    name: string,
    args: unknown,
    options: RunOptions & { wait: false },
  ): Promise<JobHandle<T>>;
  async run<T = unknown>(name: string, args: unknown, options?: RunOptions): Promise<Result<T>>;
  async run<T = unknown>(
    name: string,
    args: unknown,
    options: RunOptions = {},
  ): Promise<Result<T> | JobHandle<T>> {
    this.ensureActive();
    const catalogEntry = await this.getCatalogEntry(name);
    const request: RuntimeRequest = {
      type: "command_call",
      id: this.nextId("cmd"),
      name,
      args,
    };
    if (options.timeoutMs !== undefined) request.timeoutMs = options.timeoutMs;
    const response = await this.request<CommandResultMessage | JobAcceptedMessage>(request);

    if (catalogEntry.kind === "sync") {
      if (response.type !== MESSAGE_TYPES.commandResult) {
        throw protocolError("unexpectedResponse", "Expected command_result.");
      }
      return this.commandResult<T>(response);
    }

    if (response.type === MESSAGE_TYPES.commandResult) {
      if (response.status === "failed") this.commandResult<T>(response);
      throw protocolError("unexpectedResponse", "Expected job_accepted.");
    }
    if (response.type !== MESSAGE_TYPES.jobAccepted) {
      throw protocolError("unexpectedResponse", "Expected job_accepted.");
    }
    const handle = new JobHandle<T>(this, response.jobId, options.pollIntervalMs ?? 250);
    return options.wait === false ? handle : handle.result();
  }

  async eval<T = unknown>(code: string, timeoutMs?: number): Promise<EvalResponse<T>> {
    this.ensureActive();
    const request: RuntimeRequest = { type: "eval", id: this.nextId("eval"), code };
    if (timeoutMs !== undefined) request.timeoutMs = timeoutMs;
    const response = await this.request<EvalResultMessage | EvalErrorMessage>(request);
    if (response.type === MESSAGE_TYPES.evalError) throw HotReplError.fromEnvelope(response.error);
    const result: EvalResponse<T> = {
      hasValue: response.hasValue,
      value: response.value as T,
      durationMs: response.durationMs,
    };
    if (response.valueType !== undefined) result.valueType = response.valueType;
    if (response.truncated !== undefined) result.truncated = response.truncated;
    if (response.truncatedBytes !== undefined) result.truncatedBytes = response.truncatedBytes;
    if (response.stdout !== undefined) result.stdout = response.stdout;
    return result;
  }

  async reset(): Promise<void> {
    this.ensureActive();
    const response = await this.request<ResetResultMessage>({
      type: "reset",
      id: this.nextId("reset"),
    });
    if (!response.success) throw protocolError("resetFailed", "Runtime reset failed.");
  }

  async complete(code: string, cursor?: number): Promise<string[]> {
    this.ensureActive();
    if (!this.handshake.evaluator.supportsCompletion) {
      throw new HotReplError({
        kind: "unsupported_operation",
        code: "completionUnsupported",
        message: "The active evaluator does not support completion.",
        retryable: false,
      });
    }
    const request: RuntimeRequest = { type: "complete", id: this.nextId("complete"), code };
    if (cursor !== undefined) request.cursor = cursor;
    const response = await this.request<CompleteResultMessage>(request);
    return response.completions;
  }

  async *watch<T = unknown>(code: string): AsyncIterable<WatchTick<T>> {
    this.ensureActive();
    const request: Extract<RuntimeRequest, { type: "subscribe" }> = {
      type: "subscribe",
      id: this.nextId("watch"),
      code,
    };
    for await (const event of this.transport.watch(request)) {
      if (event.type === MESSAGE_TYPES.subscribeError) {
        throw HotReplError.fromEnvelope(event.error);
      }
      const tick: WatchTick<T> = {
        seq: event.seq,
        hasValue: event.hasValue,
        final: event.final,
        durationMs: event.durationMs,
      };
      if (event.hasValue && event.value !== undefined) tick.value = event.value as T;
      if (event.valueType !== undefined) tick.valueType = event.valueType;
      if (event.truncated !== undefined) tick.truncated = event.truncated;
      if (event.truncatedBytes !== undefined) tick.truncatedBytes = event.truncatedBytes;
      yield tick;
      if (event.final) return;
    }
  }

  async journal(
    query: { kind?: "eval" | "command"; limit?: number } = {},
  ): Promise<JournalEntry[]> {
    this.ensureActive();
    const request: RuntimeRequest = { type: "journal_query", id: this.nextId("journal") };
    if (query.kind !== undefined) request.kind = query.kind;
    if (query.limit !== undefined) request.limit = query.limit;
    const response = await this.request<JournalQueryResultMessage>(request);
    return response.entries;
  }

  artifact(ref: ArtifactRef): Artifact {
    return new Artifact(ref, this.transport);
  }

  async listCommands(): Promise<CommandSummary[]> {
    this.ensureActive();
    if (this.catalog !== undefined) return this.catalog;
    const response = await this.request<CommandsListResultMessage>({
      type: "commands_list",
      id: this.nextId("list"),
    });
    this.catalog = response.commands;
    return this.catalog;
  }

  async getCatalogEntry(name: string): Promise<CommandSummary> {
    const commands = await this.listCommands();
    const entry = commands.find((command) => command.name === name);
    if (entry === undefined) {
      throw protocolError("commandNotFound", `Command '${name}' is not registered.`);
    }
    return entry;
  }
  async describeCommand(name: string): Promise<CommandDescriptor> {
    const cached = this.descriptors.get(name);
    if (cached !== undefined) return cached;
    const response = await this.request<CommandDescribeResultMessage>({
      type: "command_describe",
      id: this.nextId("describe"),
      name,
    });
    this.descriptors.set(name, response.descriptor);
    return response.descriptor;
  }

  async pollJob<T>(jobId: string, pollIntervalMs: number): Promise<Result<T>> {
    while (true) {
      const response = await this.request<JobStatusResultMessage | JobResultMessage>({
        type: "job_status",
        id: this.nextId("status"),
        jobId,
      });
      if (response.type === MESSAGE_TYPES.jobResult) return this.jobResult<T>(response);
      if (pollIntervalMs > 0) await sleep(pollIntervalMs);
    }
  }

  nextId(prefix: string): string {
    this.sequence += 1;
    return `${prefix}-${this.sequence}`;
  }

  async request<T extends ServerMessage>(request: RuntimeRequest): Promise<T> {
    this.ensureActive();
    try {
      const response = await this.transport.request(request);
      if (response.type === MESSAGE_TYPES.error) throw HotReplError.fromEnvelope(response.error);
      return response as T;
    } catch (error) {
      if (error instanceof HotReplError) throw error;
      throw error;
    }
  }

  private commandResult<T>(response: CommandResultMessage): Result<T> {
    if (response.status === "failed") {
      throw HotReplError.fromEnvelope(response.error ?? internalError("commandFailed"));
    }
    return toResult<T>(response.output, response.artifacts, this.transport);
  }

  private jobResult<T>(response: JobResultMessage): Result<T> {
    if (response.status === "failed") {
      throw HotReplError.fromEnvelope(response.error ?? internalError("jobFailed"));
    }
    return toResult<T>(response.output, response.artifacts, this.transport);
  }

  /**
   * Close the underlying transport. Safe to call multiple times;
   * subsequent calls after the first are no-ops.
   */
  close(): void {
    if (this.closed) return;
    this.closed = true;
    this.transport.close();
  }
  private ensureActive(): void {
    if (this.evicted === undefined) return;
    throw new HotReplSessionEvicted(this.evicted);
  }
}

function protocolError(code: string, message: string): HotReplError {
  return new HotReplError({ kind: "invalid_request", code, message, retryable: false });
}

function internalError(code: string): HotReplErrorEnvelope {
  return {
    kind: "internal",
    code,
    message: "Runtime returned an error without details.",
    retryable: false,
  };
}

async function sleep(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}
