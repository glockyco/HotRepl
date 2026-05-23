import { codeToHtml } from "shiki";

export const prerender = true;

const THEME = "github-dark";

const sdkSource = `import { connect } from "@hotrepl/sdk";

const session = await connect();

// Any C# on the game's main thread:
const product = await session.eval<string>(
  "UnityEngine.Application.productName",
);

// Typed, schema-validated game command:
const preflight = await session.run<{
  writable: boolean;
  freeMb: number;
}>("archive.preflight", {});
`;

// Hand-written to match SDK return shapes verbatim:
//   EvalResponse  (packages/sdk/src/session.ts)
//   Result<T>     (packages/sdk/src/commands.ts)
const sdkResult = `// product  →  EvalResponse
{
  hasValue:   true,
  value:      "Ardenfall",
  valueType:  "System.String",
  durationMs: 7,
}

// preflight  →  Result<T>
{
  output: {
    writable: true,
    freeMb:   41213,
  },
  artifacts: {},
}
`;

const cliSource = `$ hotrepl eval 'UnityEngine.Application.productName'
Ardenfall

$ hotrepl run archive.preflight '{}'
{"writable":true,"freeMb":41213}
`;

const mcpConfigSource = `{
  "mcpServers": {
    "hotrepl": {
      "command": "npx",
      "args": ["-y", "@hotrepl/mcp"]
    }
  }
}
`;

// Mirrors the nine tools registered in packages/mcp/src/tools.ts.
const mcpToolsSource = `# Eval
hotrepl_eval
hotrepl_complete
hotrepl_reset

# Typed commands
hotrepl_list_commands
hotrepl_describe_command
hotrepl_run

# Inspection
hotrepl_info
hotrepl_read_artifact
hotrepl_journal
`;

export async function load() {
  const [sdkHtml, sdkResultHtml, cliHtml, mcpConfigHtml, mcpToolsHtml] = await Promise.all([
    codeToHtml(sdkSource, { lang: "typescript", theme: THEME }),
    codeToHtml(sdkResult, { lang: "javascript", theme: THEME }),
    codeToHtml(cliSource, { lang: "shellsession", theme: THEME }),
    codeToHtml(mcpConfigSource, { lang: "json", theme: THEME }),
    codeToHtml(mcpToolsSource, { lang: "bash", theme: THEME }),
  ]);

  return { sdkHtml, sdkResultHtml, cliHtml, mcpConfigHtml, mcpToolsHtml };
}
