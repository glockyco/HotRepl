import { connect, HotReplError } from "@hotrepl/sdk";
import type { ConnectOptions, RuntimeTransport } from "@hotrepl/sdk";
import { dispatchCommand } from "./commands/dispatch";
import { connectUntilReady } from "./commands/wait";
import { exitCodeForError } from "./exit-codes";
import type { CliFormat } from "./format";

export interface CliRunOptions {
  env?: Record<string, string | undefined>;
  runtime?: RuntimeTransport;
  signal?: AbortSignal;
  /** Writes output as it arrives instead of collecting it in the result. */
  writeStdout?: (chunk: string) => void;
}

export interface CliRunResult {
  exitCode: number;
  stderr: string;
  stdout: string;
}

interface ParsedArgs {
  args: string[];
  format: CliFormat;
  limit?: number;
  output?: string;
  url?: string;
  timeoutMs?: number;
}

export async function runCli(argv: string[], options: CliRunOptions = {}): Promise<CliRunResult> {
  let stdout = "";
  const writeStdout = options.writeStdout ?? ((chunk: string) => {
    stdout += chunk;
  });
  try {
    const parsed = parseArgs(argv);
    const connectOptions: ConnectOptions = options.runtime === undefined
      ? {}
      : { runtime: options.runtime };
    if (options.env !== undefined) connectOptions.env = options.env;
    if (parsed.url !== undefined) connectOptions.url = parsed.url;
    if (options.signal !== undefined) connectOptions.signal = options.signal;
    const session = parsed.args[0] === "wait"
      ? await connectUntilReady(connectOptions, parsed.timeoutMs)
      : await connect(connectOptions);
    try {
      const onAbort = (): void => session.close();
      options.signal?.addEventListener("abort", onAbort, { once: true });
      try {
        options.signal?.throwIfAborted();
        writeStdout(await dispatchCommand(session, parsed, writeStdout));
        options.signal?.throwIfAborted();
      } finally {
        options.signal?.removeEventListener("abort", onAbort);
      }
      return {
        exitCode: 0,
        stderr: "",
        stdout,
      };
    } finally {
      // Release the WebSocket so the Node event loop can drain. Without
      // this, `node hotrepl info` prints its output and then hangs forever
      // because the open socket keeps the loop alive.
      session.close();
    }
  } catch (error) {
    const failure = options.signal?.aborted ? options.signal.reason : error;
    return {
      exitCode: exitCodeForError(failure),
      stderr: `${errorMessage(failure)}\n`,
      stdout,
    };
  }
}

function parseArgs(argv: string[]): ParsedArgs {
  const args: string[] = [];
  let format: CliFormat = "text";
  let limit: number | undefined;
  let output: string | undefined;
  let url: string | undefined;
  let timeoutMs: number | undefined;

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--format") {
      format = parseFormat(requireValue(argv, index += 1, "--format"));
    } else if (arg === "--json") {
      format = "json";
    } else if (arg === "--jsonl") {
      format = "jsonl";
    } else if (arg === "--limit") {
      limit = Number(requireValue(argv, index += 1, "--limit"));
    } else if (arg === "--output") {
      output = requireValue(argv, index += 1, "--output");
    } else if (arg === "--url") {
      url = requireValue(argv, index += 1, "--url");
    } else if (arg === "--timeout-ms") {
      const value = requireValue(argv, index += 1, "--timeout-ms");
      timeoutMs = Number(value);
      if (!Number.isInteger(timeoutMs) || timeoutMs <= 0 || timeoutMs > 2_147_483_647) {
        throw new HotReplError({
          kind: "invalid_request",
          code: "invalidTimeout",
          message: "--timeout-ms must be a positive integer no greater than 2147483647.",
          retryable: false,
        });
      }
    } else {
      args.push(arg ?? "");
    }
  }

  const parsed: ParsedArgs = { args, format };
  if (limit !== undefined) parsed.limit = limit;
  if (output !== undefined) parsed.output = output;
  if (url !== undefined) parsed.url = url;
  if (timeoutMs !== undefined) parsed.timeoutMs = timeoutMs;
  return parsed;
}

function parseFormat(value: string): CliFormat {
  if (value === "json" || value === "jsonl" || value === "text") return value;
  throw new HotReplError({
    kind: "invalid_request",
    code: "invalidFormat",
    message: `Unsupported output format '${value}'.`,
    retryable: false,
  });
}

function requireValue(argv: string[], index: number, flag: string): string {
  const value = argv[index];
  if (value !== undefined) return value;
  throw new HotReplError({
    kind: "invalid_request",
    code: "missingFlagValue",
    message: `Missing value for ${flag}.`,
    retryable: false,
  });
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
