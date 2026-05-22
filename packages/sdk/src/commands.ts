import type { ArtifactRef, CommandDescriptor } from "@hotrepl/protocol";
import { Artifact, type ArtifactReader } from "./artifact";

export interface Result<T = unknown> {
  output: T;
  artifacts: Record<string, Artifact>;
}

export function toResult<T>(
  output: unknown,
  artifacts: Record<string, ArtifactRef> | undefined,
  reader: ArtifactReader,
): Result<T> {
  const wrapped: Record<string, Artifact> = {};
  for (const [name, ref] of Object.entries(artifacts ?? {})) {
    wrapped[name] = new Artifact(ref, reader);
  }
  return { output: output as T, artifacts: wrapped };
}

export type DescriptorCache = Map<string, CommandDescriptor>;
