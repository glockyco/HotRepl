import { describe, expect, test } from "bun:test";
import { CLI_ERROR_CODES, exitCodeForKind } from "../src/exit-codes";

const errorKinds = [
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

describe("CLI exit codes", () => {
  test("maps every v2 error kind and CLI-only failure", () => {
    const mapping = Object.fromEntries([
      ...errorKinds.map((kind) => [kind, exitCodeForKind(kind)]),
      ["server_unreachable", exitCodeForKind("server_unreachable")],
      ["session_evicted", exitCodeForKind("session_evicted")],
      ["artifact_corrupted", exitCodeForKind("artifact_corrupted")],
    ]);

    expect(mapping).toEqual(CLI_ERROR_CODES);
    expect(mapping).toMatchInlineSnapshot(`
      {
        "artifact_corrupted": 76,
        "artifact_missing": 10,
        "busy": 5,
        "cancelled": 7,
        "conflict": 4,
        "internal": 70,
        "invalid_request": 2,
        "precondition_failed": 3,
        "server_unreachable": 69,
        "session_evicted": 75,
        "timeout": 6,
        "unknown_command": 8,
        "unsupported_operation": 9,
        "validation_failed": 2,
      }
    `);
  });
  test("bin exits 69 (EX_UNAVAILABLE) when backend unreachable", () => {
    const result = Bun.spawnSync({
      cmd: [process.execPath, "packages/cli/src/bin.ts", "info"],
      env: { ...process.env, HOTREPL_URL: "ws://127.0.0.1:1" },
      cwd: import.meta.dir.replace(/\/packages\/cli\/test$/, ""),
      stderr: "pipe",
      stdout: "pipe",
    });
    expect(result.exitCode).toBe(69);
    expect(new TextDecoder().decode(result.stderr)).toContain("WebSocket connection failed");
  });

  test("bin exits 2 when --format value is invalid", () => {
    const result = Bun.spawnSync({
      cmd: [process.execPath, "packages/cli/src/bin.ts", "--format", "invalid", "info"],
      cwd: import.meta.dir.replace(/\/packages\/cli\/test$/, ""),
      stderr: "pipe",
      stdout: "pipe",
    });
    expect(result.exitCode).toBe(2);
  });
});
