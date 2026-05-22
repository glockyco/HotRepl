export const ERROR_KINDS = [
  "validation_failed",
  "precondition_failed",
  "conflict",
  "timeout",
  "cancelled",
  "busy",
  "unknown_command",
  "unsupported_operation",
  "artifact_missing",
  "invalid_request",
  "internal",
] as const;

export type ErrorKind = (typeof ERROR_KINDS)[number];
