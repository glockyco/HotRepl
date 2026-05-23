import { runCli } from "./index.js";

const result = await runCli(process.argv.slice(2), { env: process.env });
if (result.stdout.length > 0) process.stdout.write(result.stdout);
if (result.stderr.length > 0) process.stderr.write(result.stderr);
process.exit(result.exitCode);
