import { McpServer, type RegisteredTool } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import type { SessionManagerOptions } from "./session-manager";
import { SessionManager } from "./session-manager";
import { createHotReplTools, listCommandDescriptors } from "./tools";

export { SessionManager } from "./session-manager";
export { createHotReplTools, type HotReplMcpTool } from "./tools";

export interface CreateHotReplMcpServerResult {
  server: McpServer;
  /**
   * Best-effort: connect to the HotRepl backend, fetch the command list,
   * and refine hotrepl_run's annotations to match runMutates. The update
   * call on the captured RegisteredTool auto-sends
   * notifications/tools/list_changed. Errors are swallowed — conservative
   * defaults remain in place when the backend is unreachable.
   *
   * Idempotent: concurrent calls share the in-flight refresh promise.
   */
  refreshAnnotations(): Promise<void>;
}

export async function createHotReplMcpServer(
  options: SessionManagerOptions = {},
): Promise<CreateHotReplMcpServerResult> {
  const manager = new SessionManager(options);
  const server = new McpServer({ name: "hotrepl-mcp", version: "0.0.0" });

  let registeredHotreplRun: RegisteredTool | undefined;

  for (const tool of createHotReplTools(manager)) {
    const config = {
      description: tool.description,
      inputSchema: tool.inputSchema,
    };
    if (tool.annotations !== undefined) {
      Object.assign(config, { annotations: tool.annotations });
    }
    const registered = server.registerTool(
      tool.name,
      config,
      async (args) => tool.handler(args as Record<string, any>),
    );
    if (tool.name === "hotrepl_run") registeredHotreplRun = registered;
  }

  let inflight: Promise<void> | undefined;
  const refreshAnnotations = (): Promise<void> => {
    if (inflight !== undefined) return inflight;
    inflight = (async () => {
      try {
        if (registeredHotreplRun === undefined) return;
        const session = await manager.getSession();
        const commands = await listCommandDescriptors(session);
        const runMutates = commands.some((command) => command.mutatesState);
        registeredHotreplRun.update({
          annotations: {
            destructiveHint: runMutates,
            readOnlyHint: !runMutates,
          },
        });
      } catch {
        // Backend unreachable; conservative defaults remain. Refresh is
        // best-effort and never blocks startup.
      } finally {
        inflight = undefined;
      }
    })();
    return inflight;
  };

  return { server, refreshAnnotations };
}

export async function runStdioMcpServer(
  options: SessionManagerOptions = {},
): Promise<() => Promise<void>> {
  const { server, refreshAnnotations } = await createHotReplMcpServer(options);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  // Fire-and-forget: refine annotations once the backend is reachable.
  // Conservative defaults remain visible until the refresh succeeds.
  void refreshAnnotations();

  let closed = false;
  return async function shutdown(): Promise<void> {
    if (closed) return;
    closed = true;
    await server.close();
  };
}
