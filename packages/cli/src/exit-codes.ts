import { HotReplArtifactCorrupted, HotReplError, HotReplSessionEvicted } from "@hotrepl/sdk";

export const CLI_ERROR_CODES = {
  validation_failed: 2,
  invalid_request: 2,
  precondition_failed: 3,
  conflict: 4,
  busy: 5,
  timeout: 6,
  cancelled: 7,
  unknown_command: 8,
  unsupported_operation: 9,
  artifact_missing: 10,
  server_unreachable: 69,
  internal: 70,
  session_evicted: 75,
  artifact_corrupted: 76,
} as const;

export type CliErrorKind = keyof typeof CLI_ERROR_CODES;

export function exitCodeForKind(kind: string): number {
  return CLI_ERROR_CODES[kind as CliErrorKind] ?? 1;
}

export function exitCodeForError(error: unknown): number {
  if (error instanceof HotReplSessionEvicted) return exitCodeForKind("session_evicted");
  if (error instanceof HotReplArtifactCorrupted) return exitCodeForKind("artifact_corrupted");
  if (error instanceof HotReplError) return exitCodeForKind(error.kind);
  return exitCodeForKind("server_unreachable");
}
