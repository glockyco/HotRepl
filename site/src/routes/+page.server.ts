import { codeToHtml } from "shiki";

export const prerender = true;

export async function load() {
  const quickstart = `import { connect } from "@hotrepl/sdk";

const session = await connect(); // ws://127.0.0.1:18590 by default
const name = await session.eval("UnityEngine.Application.productName");
const preflight = await session.run("archive.preflight", {});

console.log(name.value, preflight.output);`;

  const quickstartHtml = await codeToHtml(quickstart, {
    lang: "typescript",
    theme: "github-dark",
  });

  return { quickstartHtml };
}
