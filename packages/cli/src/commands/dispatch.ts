import type { Session, WatchTick } from "@hotrepl/sdk";
import { type CliFormat, json, jsonl, line, printable, serializableResult } from "../format";

export interface CommandRequest {
  args: string[];
  format: CliFormat;
  limit?: number;
}

export async function dispatchCommand(session: Session, request: CommandRequest): Promise<string> {
  const [command, ...args] = request.args;
  switch (command) {
    case "info":
      return renderInfo(session, request.format);
    case "wait":
      return request.format === "json" ? json({ ready: true }) : line("ready");
    case "doctor":
      return request.format === "json" ? json({ status: "ok" }) : line("ok");
    case "eval":
      return renderEval(session, args, request.format);
    case "reset":
      await session.reset();
      return request.format === "json" ? json({ reset: true }) : line("reset");
    case "complete":
      return renderComplete(session, args, request.format);
    case "watch":
      return renderWatch(session, args, request.format);
    case "run":
      return renderRun(session, args, request.format);
    case "describe":
      return renderDescribe(session, args, request.format);
    case "artifacts":
      return renderArtifact(session, args, request.format);
    case "journal":
      return renderJournal(session, request.format, request.limit);
    default:
      throw new Error(`Unknown command '${command ?? ""}'.`);
  }
}

function renderInfo(session: Session, format: CliFormat): string {
  const handshake = session.handshake;
  if (format === "json") return json(handshake);
  return line(
    [
      `HotRepl v${handshake.protocolVersion} on ${handshake.host.name} ${handshake.host.version} (${handshake.host.platform})`,
      `evaluator: ${handshake.evaluator.name}, completion: ${
        handshake.evaluator.supportsCompletion ? "yes" : "no"
      }`,
    ].join("\n"),
  );
}

async function renderEval(session: Session, args: string[], format: CliFormat): Promise<string> {
  const result = await session.eval(args.join(" "));
  if (format === "json") return json(result);
  return line(printable(result.value));
}

async function renderComplete(
  session: Session,
  args: string[],
  format: CliFormat,
): Promise<string> {
  const [code = "", cursorText] = args;
  const completions = await session.complete(
    code,
    cursorText === undefined ? undefined : Number(cursorText),
  );
  if (format === "json") return json(completions);
  return completions.map(line).join("");
}

async function renderWatch(session: Session, args: string[], format: CliFormat): Promise<string> {
  const [code = ""] = args;
  const ticks: Array<WatchTick> = [];
  for await (const tick of session.watch(code)) ticks.push(tick);
  if (format === "jsonl") return jsonl(ticks);
  if (format === "json") return json(ticks);
  return ticks.filter((tick) => tick.hasValue).map((tick) => line(printable(tick.value))).join("");
}

async function renderRun(session: Session, args: string[], format: CliFormat): Promise<string> {
  const [name, argsJson = "{}"] = args;
  if (name === undefined) throw new Error("Missing command name.");
  const result = await session.run(name, JSON.parse(argsJson), { pollIntervalMs: 0 });
  const serialized = serializableResult(result);
  if (format === "json") return json(serialized);
  return line(printable(serialized.output));
}

async function renderDescribe(
  session: Session,
  args: string[],
  format: CliFormat,
): Promise<string> {
  const [name] = args;
  if (name === undefined) throw new Error("Missing command name.");
  const descriptor = await session.describeCommand(name);
  if (format === "json") return json(descriptor);
  return line(
    `${descriptor.name} v${descriptor.majorVersion} ${descriptor.kind} ${
      descriptor.mutatesState ? "mutating" : "readonly"
    }`,
  );
}

async function renderArtifact(
  session: Session,
  args: string[],
  format: CliFormat,
): Promise<string> {
  const [subcommand, refJson] = args;
  if (subcommand !== "read") throw new Error("Expected 'artifacts read'.");
  if (refJson === undefined) throw new Error("Missing artifact reference.");
  const artifact = session.artifact(JSON.parse(refJson) as Parameters<Session["artifact"]>[0]);
  if (format === "json") return json(await artifact.json());
  return line(await artifact.text());
}

async function renderJournal(
  session: Session,
  format: CliFormat,
  limit: number | undefined,
): Promise<string> {
  const entries = await session.journal(limit === undefined ? {} : { limit });
  if (format === "json") return json(entries);
  return entries.map((entry) =>
    line(`${entry.kind} ${entry.success ? "ok" : entry.errorKind ?? "failed"}`)
  ).join("");
}
