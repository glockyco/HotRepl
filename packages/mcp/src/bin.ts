import { runStdioMcpServer } from "./index.js";

// sysexits.h: EX_OK=0, EX_SOFTWARE=70.
const EX_OK = 0;
const EX_SOFTWARE = 70;

function fatalMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

try {
  const shutdown = await runStdioMcpServer({ env: process.env });

  const onSignal = (signal: NodeJS.Signals): void => {
    void shutdown()
      .catch(() => {})
      .finally(() => {
        // SIGINT → 130 (128 + 2), SIGTERM → 0 (graceful).
        process.exit(signal === "SIGINT" ? 130 : EX_OK);
      });
  };

  process.on("SIGINT", onSignal);
  process.on("SIGTERM", onSignal);
} catch (error) {
  // Stdio servers MUST NOT write to stdout outside the MCP protocol.
  process.stderr.write(`hotrepl-mcp: ${fatalMessage(error)}\n`);
  process.exitCode = EX_SOFTWARE;
}
