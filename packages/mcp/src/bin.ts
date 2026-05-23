import { runStdioMcpServer } from "./index.js";

// sysexits.h: EX_OK=0, EX_SOFTWARE=70.
const EX_OK = 0;
const EX_SOFTWARE = 70;

// Per the MCP spec (Lifecycle section), the client initiates shutdown by
// closing stdin, then escalates to SIGTERM and SIGKILL with reasonable
// timeouts between each step. Mirror the Python SDK's 2-second guard so a
// stuck shutdown never hangs the client.
const SHUTDOWN_WATCHDOG_MS = 2000;

function fatalMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

try {
  const shutdown = await runStdioMcpServer({ env: process.env });

  // Single-fire dispatcher: any of the shutdown triggers below race here,
  // the first one wins, and the watchdog guarantees we exit even if the
  // server's own close() pipeline stalls.
  let exiting = false;
  const exitOnce = (code: number): void => {
    if (exiting) return;
    exiting = true;
    // The unref()'d watchdog keeps the loop alive only as long as needed.
    // If the graceful path completes first, the .finally below fires
    // process.exit before this triggers.
    const watchdog = setTimeout(() => process.exit(code), SHUTDOWN_WATCHDOG_MS);
    watchdog.unref();
    void shutdown()
      .catch(() => {})
      .finally(() => {
        clearTimeout(watchdog);
        process.exit(code);
      });
  };

  // Spec-mandated primary shutdown signal: stdin EOF. The SDK's
  // StdioServerTransport listens only for 'data' and 'error' on stdin and
  // never reacts to EOF, so the bin owns this detection.
  process.stdin.once("end", () => exitOnce(EX_OK));

  // Escalation signals from the client (SIGINT for interactive Ctrl-C,
  // SIGTERM for hosted/MCP-client shutdown). Conventional exit codes:
  // SIGINT → 130 (128 + 2), SIGTERM → 0 (graceful exit).
  process.on("SIGINT", () => exitOnce(130));
  process.on("SIGTERM", () => exitOnce(EX_OK));
} catch (error) {
  // Stdio servers MUST NOT write to stdout outside the MCP protocol.
  process.stderr.write(`hotrepl-mcp: ${fatalMessage(error)}\n`);
  process.exitCode = EX_SOFTWARE;
}
