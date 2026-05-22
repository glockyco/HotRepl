import { connect } from "@hotrepl/sdk";
import type { ConformanceTarget } from "./index";

export function websocketTargetFromEnv(
  env: Record<string, string | undefined> = process.env,
): ConformanceTarget {
  const url = env.HOTREPL_CONFORMANCE_URL;
  return {
    configurable: false,
    name: "C# WebSocket host",
    skip: url === undefined,
    async create() {
      if (url === undefined) throw new Error("HOTREPL_CONFORMANCE_URL is not set.");
      return {
        dispose() {},
        session: await connect({ url }),
      };
    },
  };
}
