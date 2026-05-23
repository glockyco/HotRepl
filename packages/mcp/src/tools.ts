import type { Artifact, Result, RunOptions, Session } from "@hotrepl/sdk";
import type { CallToolResult, ToolAnnotations } from "@modelcontextprotocol/sdk/types.js";
import { z } from "zod/v4";
import type { SessionManager } from "./session-manager";

export interface HotReplMcpTool {
  annotations?: ToolAnnotations;
  description: string;
  handler: (args: Record<string, any>) => Promise<CallToolResult>;
  inputSchema: z.ZodTypeAny;
  name: string;
}

export function createHotReplTools(manager: SessionManager): HotReplMcpTool[] {
  // Conservative MCP-spec defaults for the mutating tool. The backend's
  // actual mutatesState is fetched lazily by refreshAnnotations (see
  // createHotReplMcpServer) and applied via RegisteredTool.update(), which
  // automatically emits notifications/tools/list_changed.
  const conservativeRunAnnotations = {
    destructiveHint: true,
    readOnlyHint: false,
  } satisfies ToolAnnotations;

  return [
    tool(
      "hotrepl_info",
      "Return runtime handshake and capability information.",
      z.object({}),
      safeTool(async () => {
        const current = await manager.getSession();
        return result(current.handshake);
      }),
      readOnly(),
    ),
    tool(
      "hotrepl_eval",
      "Evaluate C# code in the runtime.",
      z.object({ code: z.string(), timeoutMs: z.number().optional() }),
      safeTool(async (args) => {
        const current = await manager.getSession();
        return result(await current.eval(String(args.code), optionalNumber(args.timeoutMs)));
      }),
    ),
    tool(
      "hotrepl_reset",
      "Reset evaluator state.",
      z.object({}),
      safeTool(async () => {
        const current = await manager.getSession();
        await current.reset();
        return result({ reset: true });
      }),
    ),
    tool(
      "hotrepl_complete",
      "Return completions for C# code.",
      z.object({ code: z.string(), cursor: z.number().optional() }),
      safeTool(async (args) => {
        const current = await manager.getSession();
        return result(await current.complete(String(args.code), optionalNumber(args.cursor)));
      }),
      readOnly(),
    ),
    tool(
      "hotrepl_list_commands",
      "List typed HotRepl commands.",
      z.object({}),
      safeTool(async () => {
        const current = await manager.getSession();
        return result(await listCommandDescriptors(current));
      }),
      readOnly(),
    ),
    tool(
      "hotrepl_describe_command",
      "Describe one typed HotRepl command.",
      z.object({ name: z.string() }),
      safeTool(async (args) => {
        const current = await manager.getSession();
        return result(await current.describeCommand(String(args.name)));
      }),
      readOnly(),
    ),
    tool(
      "hotrepl_run",
      "Run a typed HotRepl command by name.",
      z.object({
        name: z.string(),
        args: z.record(z.string(), z.unknown()).default({}),
        timeoutMs: z.number().optional(),
      }),
      safeTool(async (args) => {
        const current = await manager.getSession();
        const runOptions: RunOptions = { pollIntervalMs: 0 };
        if (args.timeoutMs !== undefined) runOptions.timeoutMs = Number(args.timeoutMs);
        const output = await current.run(String(args.name), args.args ?? {}, runOptions);
        return result(serializableResult(output));
      }),
      conservativeRunAnnotations,
    ),
    tool(
      "hotrepl_read_artifact",
      "Read and verify a HotRepl artifact reference.",
      z.object({ ref: z.unknown() }),
      safeTool(async (args) => {
        const current = await manager.getSession();
        const artifact = current.artifact(args.ref as Parameters<Session["artifact"]>[0]);
        return result({ text: await artifact.text() });
      }),
      readOnly(),
    ),
    tool(
      "hotrepl_journal",
      "Query recent eval and command journal entries.",
      z.object({ kind: z.enum(["eval", "command"]).optional(), limit: z.number().optional() }),
      safeTool(async (args) => {
        const current = await manager.getSession();
        const query: Parameters<Session["journal"]>[0] = {};
        if (args.kind === "eval" || args.kind === "command") query.kind = args.kind;
        if (args.limit !== undefined) query.limit = Number(args.limit);
        return result(await current.journal(query));
      }),
      readOnly(),
    ),
  ];
}

function tool(
  name: string,
  description: string,
  inputSchema: z.ZodTypeAny,
  handler: (args: Record<string, any>) => Promise<CallToolResult>,
  annotations?: ToolAnnotations,
): HotReplMcpTool {
  return annotations === undefined
    ? { name, description, inputSchema, handler }
    : { name, description, inputSchema, handler, annotations };
}

export async function listCommandDescriptors(session: Session) {
  const response = await session.request({ type: "commands_list", id: "mcp-list-commands" });
  if (response.type !== "commands_list_result") {
    throw new Error(`Expected commands_list_result, got ${response.type}.`);
  }
  return response.commands;
}

function result(value: unknown): CallToolResult {
  return {
    content: [{ type: "text", text: JSON.stringify(value) }],
    structuredContent: value as Record<string, unknown>,
  };
}

function readOnly(): ToolAnnotations {
  return { readOnlyHint: true };
}

function optionalNumber(value: unknown): number | undefined {
  return value === undefined ? undefined : Number(value);
}

function serializableResult<T>(
  commandResult: Result<T>,
): { artifacts: Record<string, Artifact["ref"]>; output: T } {
  const artifacts: Record<string, Artifact["ref"]> = {};
  for (const [name, artifact] of Object.entries(commandResult.artifacts)) {
    artifacts[name] = artifact.ref;
  }
  return { output: commandResult.output, artifacts };
}
const HOTREPL_NOT_REACHABLE_MESSAGE =
  "HotRepl is not reachable at the configured URL. Make sure your Unity game with the HotRepl plugin (BepInEx) or mod (MelonLoader) is running.";

function isConnectionFailure(error: unknown): boolean {
  if (!(error instanceof Error)) return false;
  const message = error.message;
  return (
    message.includes("WebSocket connection failed")
    || message.includes("WebSocket connection closed")
    || message.includes("ECONNREFUSED")
    || message.includes("ENOTFOUND")
  );
}

function formatBackendError(error: unknown): string {
  if (isConnectionFailure(error)) return HOTREPL_NOT_REACHABLE_MESSAGE;
  if (error instanceof Error) return error.message;
  return String(error);
}

function safeTool<T extends (args: Record<string, any>) => Promise<CallToolResult>>(
  handler: T,
): T {
  return (async (args: Record<string, any>) => {
    try {
      return await handler(args);
    } catch (error) {
      return {
        content: [{ type: "text" as const, text: formatBackendError(error) }],
        isError: true,
      };
    }
  }) as T;
}
