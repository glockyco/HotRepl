import { families, sharedTypes, validateAllExamples } from "$lib/data/protocol";
import { codeToHtml } from "shiki";
import type { PageServerLoad } from "./$types";

export const prerender = true;

export const load: PageServerLoad = async () => {
  // Fail the build if any example is structurally invalid.
  validateAllExamples(families, sharedTypes);

  const highlightJson = (code: string) => codeToHtml(code, { lang: "json", theme: "github-dark" });

  const highlightedFamilies = await Promise.all(
    families.map(async (family) => ({
      ...family,
      messages: await Promise.all(
        family.messages.map(async (msg) => ({
          ...msg,
          exampleHtml: await highlightJson(msg.example),
          // JSON.stringify produces clean JSON Schema in TypeBox 1.x (non-enumerable internals)
          schemaHtml: await highlightJson(JSON.stringify(msg.schema, null, 2)),
        })),
      ),
    })),
  );

  const highlightedSharedTypes = await Promise.all(
    sharedTypes.map(async (t) => ({
      ...t,
      exampleHtml: await highlightJson(t.example),
      schemaHtml: await highlightJson(JSON.stringify(t.schema, null, 2)),
    })),
  );

  return {
    families: highlightedFamilies,
    sharedTypes: highlightedSharedTypes,
  };
};
