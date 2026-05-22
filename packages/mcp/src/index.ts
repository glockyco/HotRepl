#!/usr/bin/env bun
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import type { SessionManagerOptions } from "./session-manager";
import { SessionManager } from "./session-manager";
import { createHotReplTools } from "./tools";

export { SessionManager } from "./session-manager";
export { createHotReplTools, type HotReplMcpTool } from "./tools";

export async function createHotReplMcpServer(options: SessionManagerOptions = {}): Promise<McpServer> {
  const manager = new SessionManager(options);
  const server = new McpServer({ name: "hotrepl-mcp", version: "0.0.0" });
  for (const tool of await createHotReplTools(manager)) {
    const config = {
      description: tool.description,
      inputSchema: tool.inputSchema,
    };
    if (tool.annotations !== undefined) {
      Object.assign(config, { annotations: tool.annotations });
    }
    server.registerTool(tool.name, config, async (args) => tool.handler(args as Record<string, any>));
  }
  return server;
}

export async function runStdioMcpServer(options: SessionManagerOptions = {}): Promise<void> {
  const server = await createHotReplMcpServer(options);
  await server.connect(new StdioServerTransport());
}

if (import.meta.main) {
  await runStdioMcpServer({ env: process.env });
}
