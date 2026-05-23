import { connect, HotReplError } from "@hotrepl/sdk";
import type { ConnectOptions, RuntimeTransport } from "@hotrepl/sdk";
import { dispatchCommand } from "./commands/dispatch";
import { exitCodeForError } from "./exit-codes";
import type { CliFormat } from "./format";

export interface CliRunOptions {
  env?: Record<string, string | undefined>;
  runtime?: RuntimeTransport;
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
  url?: string;
}

export async function runCli(argv: string[], options: CliRunOptions = {}): Promise<CliRunResult> {
  try {
    const parsed = parseArgs(argv);
    const connectOptions: ConnectOptions = options.runtime === undefined
      ? {}
      : { runtime: options.runtime };
    if (options.env !== undefined) connectOptions.env = options.env;
    if (parsed.url !== undefined) connectOptions.url = parsed.url;
    const session = await connect(connectOptions);
    return {
      exitCode: 0,
      stderr: "",
      stdout: await dispatchCommand(session, parsed),
    };
  } catch (error) {
    return {
      exitCode: exitCodeForError(error),
      stderr: `${errorMessage(error)}\n`,
      stdout: "",
    };
  }
}

function parseArgs(argv: string[]): ParsedArgs {
  const args: string[] = [];
  let format: CliFormat = "text";
  let limit: number | undefined;
  let url: string | undefined;

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
    } else if (arg === "--url") {
      url = requireValue(argv, index += 1, "--url");
    } else {
      args.push(arg ?? "");
    }
  }

  const parsed: ParsedArgs = { args, format };
  if (limit !== undefined) parsed.limit = limit;
  if (url !== undefined) parsed.url = url;
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
