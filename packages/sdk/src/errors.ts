import type { ErrorKind, HotReplErrorEnvelope, SessionEvictedMessage } from "@hotrepl/protocol";

export interface HotReplErrorInput {
  kind: ErrorKind;
  code: string;
  message: string;
  retryable: boolean;
  details?: unknown;
}

export class HotReplError extends Error {
  readonly kind: ErrorKind;
  readonly code: string;
  readonly retryable: boolean;
  readonly details: unknown;

  constructor(input: HotReplErrorInput) {
    super(input.message);
    this.name = "HotReplError";
    this.kind = input.kind;
    this.code = input.code;
    this.retryable = input.retryable;
    this.details = input.details;
  }

  static fromEnvelope(error: HotReplErrorEnvelope): HotReplError {
    return new HotReplError(error);
  }
}

export class HotReplArtifactCorrupted extends HotReplError {
  constructor(message: string) {
    super({
      kind: "artifact_missing",
      code: "artifactHashMismatch",
      message,
      retryable: false,
    });
    this.name = "HotReplArtifactCorrupted";
  }
}

export class HotReplSessionEvicted extends HotReplError {
  readonly event: SessionEvictedMessage;

  constructor(event: SessionEvictedMessage) {
    super({
      kind: "conflict",
      code: "sessionEvicted",
      message: `Session was evicted: ${event.reason}.`,
      retryable: false,
    });
    this.name = "HotReplSessionEvicted";
    this.event = event;
  }
}
