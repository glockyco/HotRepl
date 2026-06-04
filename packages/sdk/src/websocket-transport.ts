import { MESSAGE_TYPES } from "@hotrepl/protocol";
import type {
  ArtifactRef,
  AssemblyReloadMessage,
  HandshakeMessage,
  ServerMessage,
  SessionEvictedMessage,
} from "@hotrepl/protocol";
import { HotReplError, HotReplSessionEvicted } from "./errors";
import type { RuntimeRequest, RuntimeTransport, WatchWireMessage } from "./session";

type PendingRequest = {
  reject: (reason: unknown) => void;
  resolve: (message: ServerMessage) => void;
};

type QueueWaiter<T> = {
  reject: (reason: unknown) => void;
  resolve: (result: IteratorResult<T>) => void;
};

class AsyncMessageQueue<T> implements AsyncIterable<T> {
  private readonly values: T[] = [];
  private readonly waiters: Array<QueueWaiter<T>> = [];
  private failure: unknown;
  private isClosed = false;

  push(value: T): void {
    const waiter = this.waiters.shift();
    if (waiter !== undefined) {
      waiter.resolve({ done: false, value });
      return;
    }
    this.values.push(value);
  }

  close(): void {
    this.isClosed = true;
    while (this.waiters.length > 0) {
      this.waiters.shift()?.resolve({ done: true, value: undefined });
    }
  }

  fail(error: unknown): void {
    this.failure = error;
    this.isClosed = true;
    while (this.waiters.length > 0) {
      this.waiters.shift()?.reject(error);
    }
  }

  async *[Symbol.asyncIterator](): AsyncIterator<T> {
    while (true) {
      if (this.values.length > 0) {
        yield this.values.shift() as T;
        continue;
      }
      if (this.failure !== undefined) throw this.failure;
      if (this.isClosed) return;
      const result = await new Promise<IteratorResult<T>>((resolve, reject) => {
        this.waiters.push({ reject, resolve });
      });
      if (result.done === true) return;
      yield result.value;
    }
  }
}

export class WebSocketTransport implements RuntimeTransport {
  private readonly pending = new Map<string, PendingRequest>();
  private readonly subscriptions = new Map<string, AsyncMessageQueue<WatchWireMessage>>();
  private readonly evictionListeners = new Set<(event: SessionEvictedMessage) => void>();
  private readonly reloadListeners = new Set<(event: AssemblyReloadMessage) => void>();
  private cancelSeq = 0;
  private handshakeMessage: HandshakeMessage | undefined;
  private evicted: SessionEvictedMessage | undefined;

  private constructor(private readonly socket: WebSocket) {}

  static connect(url: string): Promise<WebSocketTransport> {
    const transport = new WebSocketTransport(new WebSocket(url));
    return transport.open();
  }

  handshake(): Promise<HandshakeMessage> {
    if (this.handshakeMessage === undefined) {
      throw new HotReplError({
        kind: "precondition_failed",
        code: "handshakeMissing",
        message: "Runtime handshake has not been received.",
        retryable: false,
      });
    }
    return Promise.resolve(this.handshakeMessage);
  }

  request(request: RuntimeRequest): Promise<ServerMessage> {
    this.ensureAvailable();
    return new Promise((resolve, reject) => {
      this.pending.set(request.id, { reject, resolve });
      try {
        this.socket.send(JSON.stringify(request));
      } catch (error) {
        this.pending.delete(request.id);
        reject(error);
      }
    });
  }

  async *watch(
    request: Extract<RuntimeRequest, { type: "subscribe" }>,
  ): AsyncIterable<WatchWireMessage> {
    this.ensureAvailable();
    const queue = new AsyncMessageQueue<WatchWireMessage>();
    this.subscriptions.set(request.id, queue);
    let final = false;
    try {
      this.socket.send(JSON.stringify(request));
      for await (const message of queue) {
        yield message;
        if (message.final) {
          final = true;
          return;
        }
      }
    } finally {
      this.subscriptions.delete(request.id);
      // If the consumer stopped early, tell the server to end the subscription.
      if (!final) this.cancel(request.id);
    }
  }

  async readArtifact(ref: ArtifactRef): Promise<Uint8Array> {
    if (ref.path !== undefined) {
      return new Uint8Array(await Bun.file(ref.path).arrayBuffer());
    }

    const response = await fetch(ref.uri);
    if (!response.ok) {
      throw new HotReplError({
        kind: "artifact_missing",
        code: "artifactReadFailed",
        message: `Artifact ${ref.uri} could not be read.`,
        retryable: false,
      });
    }
    return new Uint8Array(await response.arrayBuffer());
  }

  onSessionEvicted(listener: (event: SessionEvictedMessage) => void): () => void {
    this.evictionListeners.add(listener);
    return () => this.evictionListeners.delete(listener);
  }

  onAssemblyReload(listener: (event: AssemblyReloadMessage) => void): () => void {
    this.reloadListeners.add(listener);
    return () => this.reloadListeners.delete(listener);
  }

  cancel(targetId: string): void {
    if (this.socket.readyState !== WebSocket.OPEN) return;
    this.cancelSeq += 1;
    try {
      this.socket.send(
        JSON.stringify({ type: MESSAGE_TYPES.cancel, id: `cancel-${this.cancelSeq}`, targetId }),
      );
    } catch {
      // Cancellation is best-effort; a closing socket simply drops it.
    }
  }
  close(): void {
    this.socket.close();
  }

  private open(): Promise<WebSocketTransport> {
    return new Promise((resolve, reject) => {
      const failOpen = (error: unknown): void => {
        reject(error);
        this.failAll(error);
      };

      this.socket.addEventListener("message", (event) => {
        void this.handleSocketMessage(event.data).then(resolve, failOpen);
      });
      this.socket.addEventListener("error", () => {
        failOpen(new Error("HotRepl WebSocket connection failed."));
      });
      // Belt-and-braces: undici's internal Node EventEmitter dispatches the
      // 'error' event independently of addEventListener. Without a Node-style
      // listener, EventEmitter re-throws synchronously and surfaces as an
      // uncaughtException. The W3C listener above is still the one that runs
      // user logic; this listener exists only to suppress the EventEmitter
      // re-throw. In a browser this property is undefined and the cast
      // short-circuits at the optional call.
      (this.socket as unknown as { on?: (e: string, fn: () => void) => void })
        .on?.("error", () => {});
      this.socket.addEventListener("close", () => {
        this.failAll(new Error("HotRepl WebSocket connection closed."));
      });
    });
  }

  private async handleSocketMessage(data: unknown): Promise<WebSocketTransport> {
    const message = JSON.parse(await messageText(data)) as ServerMessage;
    if (message.type === MESSAGE_TYPES.handshake) {
      this.handshakeMessage = message;
      return this;
    }

    this.dispatch(message);
    return this;
  }

  private dispatch(message: ServerMessage): void {
    if (message.type === MESSAGE_TYPES.sessionEvicted) {
      this.evicted = message;
      const error = new HotReplSessionEvicted(message);
      for (const listener of this.evictionListeners) listener(message);
      this.failAll(error);
      return;
    }

    if (message.type === MESSAGE_TYPES.assemblyReload) {
      for (const listener of this.reloadListeners) listener(message);
      return;
    }

    if (message.type === MESSAGE_TYPES.error && message.id !== undefined) {
      const queue = this.subscriptions.get(message.id);
      if (queue !== undefined) {
        this.subscriptions.delete(message.id);
        queue.fail(HotReplError.fromEnvelope(message.error));
        return;
      }
      const pending = this.pending.get(message.id);
      if (pending !== undefined) {
        this.pending.delete(message.id);
        pending.reject(HotReplError.fromEnvelope(message.error));
        return;
      }
    }

    if (
      message.type === MESSAGE_TYPES.subscribeResult
      || message.type === MESSAGE_TYPES.subscribeError
    ) {
      const queue = this.subscriptions.get(message.id);
      if (queue === undefined) return;
      queue.push(message);
      if (message.final) {
        queue.close();
        this.subscriptions.delete(message.id);
      }
      return;
    }

    if (!("id" in message)) return;
    const pending = this.pending.get(message.id);
    if (pending === undefined) return;
    this.pending.delete(message.id);
    pending.resolve(message);
  }

  private ensureAvailable(): void {
    if (this.evicted !== undefined) throw new HotReplSessionEvicted(this.evicted);
    if (this.socket.readyState !== WebSocket.OPEN) {
      throw new HotReplError({
        kind: "precondition_failed",
        code: "webSocketNotOpen",
        message: "HotRepl WebSocket is not open.",
        retryable: true,
      });
    }
  }

  private failAll(error: unknown): void {
    for (const pending of this.pending.values()) pending.reject(error);
    this.pending.clear();
    for (const queue of this.subscriptions.values()) queue.fail(error);
    this.subscriptions.clear();
  }
}

async function messageText(data: unknown): Promise<string> {
  if (typeof data === "string") return data;
  if (data instanceof ArrayBuffer) return new TextDecoder().decode(data);
  if (ArrayBuffer.isView(data)) return new TextDecoder().decode(data);
  if (data instanceof Blob) return data.text();
  return String(data);
}
