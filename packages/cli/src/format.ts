import type { Artifact, Result } from "@hotrepl/sdk";

export type CliFormat = "json" | "jsonl" | "text";

export function line(text: string): string {
  return `${text}\n`;
}

export function json(value: unknown): string {
  return `${JSON.stringify(value)}\n`;
}

export function jsonl(values: Iterable<unknown>): string {
  let output = "";
  for (const value of values) output += json(value);
  return output;
}

export function printable(value: unknown): string {
  if (value === undefined) return "";
  if (typeof value === "string") return value;
  return JSON.stringify(value);
}

export function serializableResult<T>(result: Result<T>): { artifacts: Record<string, Artifact["ref"]>; output: T } {
  const artifacts: Record<string, Artifact["ref"]> = {};
  for (const [name, artifact] of Object.entries(result.artifacts)) artifacts[name] = artifact.ref;
  return { output: result.output, artifacts };
}
