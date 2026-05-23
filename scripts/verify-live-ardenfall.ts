#!/usr/bin/env bun
// Local-only live verification harness. NOT runnable in CI — CI cannot launch
// a Unity game. This script exists to give a maintainer one command that
// exercises the published-tarball bins (CLI + MCP) against a real HotRepl
// backend, after they have manually launched the game.
//
// Chosen test bed: Ardenfall Demo with the ardenfall-compendium mod. The
// harness is hard-coded to the Ardenfall expectations (productName contains
// "Ardenfall", `compendium.info` is a registered read-only command) because
// that is the maintainer's reference setup; adapt the assertions for another
// game by editing the `record(...)` calls below.
//
// Prerequisites (do these BEFORE running this script):
//   1. Tarball install at /tmp/hotrepl-smoke. From the HotRepl repo root:
//        bun run build
//        for p in protocol sdk cli mcp; do
//          (cd packages/$p && npm pack --pack-destination=/tmp/hotrepl-pack)
//        done
//        mkdir -p /tmp/hotrepl-smoke && cd /tmp/hotrepl-smoke
//        npm install --no-fund \
//          file:/tmp/hotrepl-pack/hotrepl-protocol-2.0.0.tgz \
//          file:/tmp/hotrepl-pack/hotrepl-sdk-2.0.0.tgz \
//          file:/tmp/hotrepl-pack/hotrepl-cli-2.0.0.tgz \
//          file:/tmp/hotrepl-pack/hotrepl-mcp-2.0.0.tgz
//   2. ardenfall-compendium plugin deployed:
//        cd ~/Projects/ardenfall-compendium && bun run hotrepl:setup
//   3. Game running and reachable:
//        cd ~/Projects/ardenfall-compendium && bun run hotrepl:launch
//
// Usage:
//   bun scripts/verify-live-ardenfall.ts [--smoke-root /tmp/hotrepl-smoke]
//
// Exit codes:
//   0  every check passed
//   1  one or more checks failed (details on stderr)
//   2  preconditions missing (smoke install absent, port never opened)

import { spawn, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { connect } from "node:net";
import { resolve } from "node:path";

const URL = process.env.HOTREPL_URL ?? "ws://127.0.0.1:18590";
const args = process.argv.slice(2);
const smokeRoot = resolve(
  args[args.indexOf("--smoke-root") + 1] ?? "/tmp/hotrepl-smoke",
);
const hotreplBin = `${smokeRoot}/node_modules/.bin/hotrepl`;
const mcpBin = `${smokeRoot}/node_modules/.bin/hotrepl-mcp`;

if (!existsSync(hotreplBin) || !existsSync(mcpBin)) {
  console.error(
    `Smoke install not found at ${smokeRoot}. Run the tarball install:\n`
      + `  cd ~/Projects/HotRepl && bun run build\n`
      + `  for p in protocol sdk cli mcp; do (cd packages/$p && npm pack --pack-destination=/tmp/hotrepl-pack); done\n`
      + `  mkdir -p ${smokeRoot} && cd ${smokeRoot} && npm install --no-fund \\\n`
      + `    file:/tmp/hotrepl-pack/hotrepl-protocol-2.0.0.tgz \\\n`
      + `    file:/tmp/hotrepl-pack/hotrepl-sdk-2.0.0.tgz \\\n`
      + `    file:/tmp/hotrepl-pack/hotrepl-cli-2.0.0.tgz \\\n`
      + `    file:/tmp/hotrepl-pack/hotrepl-mcp-2.0.0.tgz`,
  );
  process.exit(2);
}

const portMatch = /ws:\/\/([^:/]+):(\d+)/.exec(URL);
if (!portMatch) {
  console.error(`Cannot parse host:port from HOTREPL_URL=${URL}`);
  process.exit(2);
}
const [, host, portStr] = portMatch;
const port = Number(portStr);

type CheckResult = { name: string; ok: boolean; detail: string };
const results: CheckResult[] = [];
function record(name: string, ok: boolean, detail: string): void {
  results.push({ name, ok, detail });
  console.log(`  ${ok ? "✓" : "✗"} ${name}${detail ? ` — ${detail}` : ""}`);
}

async function probePort(): Promise<boolean> {
  return new Promise((res) => {
    const socket = connect({ host, port, timeout: 1000 });
    socket.once("connect", () => {
      socket.destroy();
      res(true);
    });
    socket.once("error", () => res(false));
    socket.once("timeout", () => {
      socket.destroy();
      res(false);
    });
  });
}

console.log(`Waiting for HotRepl on ${URL} (up to 120s) …`);
const deadline = Date.now() + 120_000;
let connected = false;
while (Date.now() < deadline) {
  if (await probePort()) {
    connected = true;
    break;
  }
  await new Promise((r) => setTimeout(r, 1000));
}
if (!connected) {
  console.error(`Backend never opened port ${port} within 120s.`);
  process.exit(2);
}
console.log("Backend reachable.\n");

// ---- CLI checks -----------------------------------------------------------

function runCli(...cliArgs: string[]): { stdout: string; stderr: string; code: number } {
  const r = spawnSync(hotreplBin, cliArgs, {
    env: { ...process.env, HOTREPL_URL: URL },
    encoding: "utf8",
  });
  return { stdout: r.stdout ?? "", stderr: r.stderr ?? "", code: r.status ?? -1 };
}

console.log("CLI checks:");

{
  const r = runCli("info", "--json");
  let parsed: { host?: { name?: string } } | undefined;
  try {
    parsed = JSON.parse(r.stdout);
  } catch {
    // leave parsed undefined
  }
  record(
    "hotrepl info --json returns a handshake",
    r.code === 0 && typeof parsed?.host?.name === "string",
    `exit=${r.code}, host.name=${parsed?.host?.name ?? "(missing)"}`,
  );
}

{
  const r = runCli("eval", "UnityEngine.Application.productName");
  const containsArdenfall = /Ardenfall/.test(r.stdout);
  record(
    "hotrepl eval Application.productName mentions Ardenfall",
    r.code === 0 && containsArdenfall,
    `exit=${r.code}, stdout="${r.stdout.trim().slice(0, 80)}"`,
  );
}

{
  const r = runCli("run", "compendium.info", "{}");
  // compendium.info is read-only and must succeed with structured output.
  record(
    "hotrepl run compendium.info returns success",
    r.code === 0 && r.stdout.length > 0,
    `exit=${r.code}, stdout_bytes=${r.stdout.length}`,
  );
}

// ---- MCP stdio round-trip -------------------------------------------------

console.log("\nMCP stdio checks:");

async function mcpRoundTrip(): Promise<{
  toolsCount: number;
  evalResult: string;
  runResult: string;
  listChangedSeen: boolean;
  stderr: string;
  code: number;
}> {
  return new Promise((res, rej) => {
    const child = spawn(mcpBin, [], {
      env: { ...process.env, HOTREPL_URL: URL },
      stdio: ["pipe", "pipe", "pipe"],
    });
    const stdoutChunks: string[] = [];
    const stderrChunks: string[] = [];
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (c: string) => stdoutChunks.push(c));
    child.stderr.on("data", (c: string) => stderrChunks.push(c));

    const send = (msg: Record<string, unknown>): void => {
      child.stdin.write(JSON.stringify(msg) + "\n");
    };

    send({
      jsonrpc: "2.0",
      id: 1,
      method: "initialize",
      params: {
        protocolVersion: "2024-11-05",
        capabilities: {},
        clientInfo: { name: "verify-live", version: "0.0.0" },
      },
    });
    send({ jsonrpc: "2.0", method: "notifications/initialized" });
    send({ jsonrpc: "2.0", id: 2, method: "tools/list" });
    send({
      jsonrpc: "2.0",
      id: 3,
      method: "tools/call",
      params: {
        name: "hotrepl_eval",
        arguments: { code: "UnityEngine.Application.productName" },
      },
    });
    send({
      jsonrpc: "2.0",
      id: 4,
      method: "tools/call",
      params: {
        name: "hotrepl_run",
        arguments: { name: "compendium.info", args: {} },
      },
    });

    // Give the refresh notification time to land, then close stdin.
    setTimeout(() => child.stdin.end(), 3000);

    child.on("exit", (code) => {
      const stdout = stdoutChunks.join("");
      const stderr = stderrChunks.join("");
      const lines = stdout.split("\n").filter((l) => l.trim().length > 0);
      let toolsCount = 0;
      let evalResult = "";
      let runResult = "";
      let listChangedSeen = false;
      for (const line of lines) {
        try {
          const m = JSON.parse(line) as {
            id?: number;
            method?: string;
            result?: {
              tools?: unknown[];
              content?: { text: string }[];
              isError?: boolean;
            };
          };
          if (m.method === "notifications/tools/list_changed") listChangedSeen = true;
          if (m.id === 2 && Array.isArray(m.result?.tools)) toolsCount = m.result.tools.length;
          if (m.id === 3 && m.result?.content) {
            evalResult = m.result.content[0]?.text ?? "";
          }
          if (m.id === 4 && m.result?.content) {
            runResult = m.result.content[0]?.text ?? "";
          }
        } catch {
          // ignore non-JSON lines
        }
      }
      res({
        toolsCount,
        evalResult,
        runResult,
        listChangedSeen,
        stderr,
        code: code ?? -1,
      });
    });
    child.on("error", rej);
  });
}

const mcp = await mcpRoundTrip();
record(
  "MCP tools/list returns 9 tools",
  mcp.toolsCount === 9,
  `count=${mcp.toolsCount}`,
);
record(
  "MCP hotrepl_eval Application.productName mentions Ardenfall",
  /Ardenfall/.test(mcp.evalResult),
  `result="${mcp.evalResult.slice(0, 80)}"`,
);
record(
  "MCP hotrepl_run compendium.info returns non-empty result",
  mcp.runResult.length > 0 && !mcp.runResult.toLowerCase().includes("not reachable"),
  `bytes=${mcp.runResult.length}`,
);
record(
  "MCP background refresh emitted notifications/tools/list_changed",
  mcp.listChangedSeen,
  mcp.listChangedSeen ? "seen" : "not seen (annotation values may still have changed)",
);
record(
  "MCP stderr empty (no stack traces on shutdown)",
  mcp.stderr.length === 0,
  mcp.stderr ? `stderr="${mcp.stderr.slice(0, 120).replace(/\n/g, " ")}"` : "empty",
);
record(
  "MCP exited cleanly (exit 0)",
  mcp.code === 0,
  `code=${mcp.code}`,
);

// ---- Summary --------------------------------------------------------------

const failed = results.filter((r) => !r.ok);
console.log(
  `\n${results.length - failed.length}/${results.length} checks passed.`,
);
if (failed.length > 0) {
  console.error("Failures:");
  for (const f of failed) console.error(`  - ${f.name}: ${f.detail}`);
  process.exit(1);
}
process.exit(0);
