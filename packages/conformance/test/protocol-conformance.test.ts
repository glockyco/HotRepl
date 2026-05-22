import { test } from "bun:test";
import { fakeRuntimeTarget } from "../src/fake-runtime-target";
import { runConformance } from "../src/index";
import { websocketTargetFromEnv } from "../src/websocket-target";

runConformance(fakeRuntimeTarget());

const websocketTarget = websocketTargetFromEnv();
if (websocketTarget.skip) {
  test.skip("real C# host conformance is skipped without HOTREPL_CONFORMANCE_URL", () => {});
} else {
  runConformance(websocketTarget);
}
