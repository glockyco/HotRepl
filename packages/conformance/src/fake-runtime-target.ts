import { connect } from "@hotrepl/sdk";
import { FakeRuntime } from "@hotrepl/testing";
import type { FakeRuntimeOptions } from "@hotrepl/testing";
import type { ConformanceCreateOptions, ConformanceTarget } from "./index";

export function fakeRuntimeTarget(): ConformanceTarget {
  return {
    configurable: true,
    name: "FakeRuntime",
    async create(options: ConformanceCreateOptions = {}) {
      const runtimeOptions: FakeRuntimeOptions = {};
      if (options.limits !== undefined) runtimeOptions.limits = options.limits;
      if (options.supportsCompletion !== undefined) {
        runtimeOptions.supportsCompletion = options.supportsCompletion;
      }
      const runtime = new FakeRuntime(runtimeOptions);
      options.configure?.(runtime);
      return {
        dispose() {},
        runtime,
        session: await connect({ runtime }),
      };
    },
  };
}
