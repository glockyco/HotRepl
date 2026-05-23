import { runCli } from "./index.js";

// sysexits.h: EX_SOFTWARE=70.
const EX_SOFTWARE = 70;

function fatalMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

// Install early — before any async work — so SIGINT always exits 130
// (128 + signal number) regardless of where the CLI is in its lifecycle.
process.on("SIGINT", () => {
  process.exit(130);
});

try {
  const result = await runCli(process.argv.slice(2), { env: process.env });
  if (result.stdout.length > 0) process.stdout.write(result.stdout);
  if (result.stderr.length > 0) process.stderr.write(result.stderr);
  // Set exitCode and return so Node drains stdout/stderr naturally before
  // the event loop exits. Avoids the truncation hazard of process.exit().
  process.exitCode = result.exitCode;
} catch (error) {
  process.stderr.write(`hotrepl: ${fatalMessage(error)}\n`);
  process.exitCode = EX_SOFTWARE;
}
