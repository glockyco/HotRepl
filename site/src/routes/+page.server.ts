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

export async function load() {
  const [sdkHtml, sdkResultHtml, cliHtml] = await Promise.all([
    codeToHtml(sdkSource, { lang: "typescript", theme: THEME }),
    codeToHtml(sdkResult, { lang: "javascript", theme: THEME }),
    codeToHtml(cliSource, { lang: "shellsession", theme: THEME }),
  ]);

  return { sdkHtml, sdkResultHtml, cliHtml };
}
