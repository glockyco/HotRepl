import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { HandshakeMessageSchema } from "../src";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const schemaDir = join(scriptDir, "..", "schemas");

await mkdir(schemaDir, { recursive: true });
await writeFile(
  join(schemaDir, "handshake.schema.json"),
  `${JSON.stringify(HandshakeMessageSchema, null, 2)}\n`,
);
