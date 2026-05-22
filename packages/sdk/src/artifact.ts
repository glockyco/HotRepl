import type { ArtifactRef } from "@hotrepl/protocol";
import { HotReplArtifactCorrupted } from "./errors";

export interface ArtifactReader {
  readArtifact(ref: ArtifactRef): Promise<Uint8Array>;
}

export class Artifact {
  readonly ref: ArtifactRef;
  private readonly reader: ArtifactReader;

  constructor(ref: ArtifactRef, reader: ArtifactReader) {
    this.ref = ref;
    this.reader = reader;
  }

  async bytes(): Promise<Uint8Array> {
    const bytes = await this.reader.readArtifact(this.ref);
    const actual = await sha256Hex(bytes);
    if (actual !== this.ref.sha256) {
      throw new HotReplArtifactCorrupted(
        `Artifact ${this.ref.uri} hash mismatch: expected ${this.ref.sha256}, got ${actual}.`,
      );
    }
    return bytes;
  }

  async text(): Promise<string> {
    return new TextDecoder().decode(await this.bytes());
  }

  async json<T = unknown>(): Promise<T> {
    return JSON.parse(await this.text()) as T;
  }

  async open(): Promise<{ path?: string; uri: string }> {
    await this.bytes();
    return this.ref.path === undefined
      ? { uri: this.ref.uri }
      : { path: this.ref.path, uri: this.ref.uri };
  }
}

export async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", webCryptoSource(bytes));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function webCryptoSource(bytes: Uint8Array): Uint8Array<ArrayBuffer> {
  if (bytes.buffer instanceof ArrayBuffer) {
    return bytes as Uint8Array<ArrayBuffer>;
  }

  return new Uint8Array(bytes);
}
