import { runCli } from "./index.js";

// sysexits.h: EX_SOFTWARE=70.
const EX_SOFTWARE = 70;

function fatalMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

// Handle interruption through the active session so pending output can drain.
const interruption = new AbortController();
process.on("SIGINT", () => {
  // A second interruption must still terminate if graceful cleanup stalls.
  if (interruption.signal.aborted) process.exit(130);
  interruption.abort(new Error("Interrupted."));
});

try {
  const result = await runCli(process.argv.slice(2), {
    env: process.env,
    signal: interruption.signal,
    writeStdout: (chunk) => {
      process.stdout.write(chunk);
    },
  });
  if (interruption.signal.aborted) {
    process.exitCode = 130;
  } else {
    if (result.stderr.length > 0) process.stderr.write(result.stderr);
    // Set exitCode and return so Node drains stdout/stderr naturally before
    // the event loop exits. Avoids the truncation hazard of process.exit().
    process.exitCode = result.exitCode;
  }
} catch (error) {
  process.stderr.write(`hotrepl: ${fatalMessage(error)}\n`);
  process.exitCode = EX_SOFTWARE;
}
