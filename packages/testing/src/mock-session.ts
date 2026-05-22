import { connect, type Session } from "@hotrepl/sdk";
import type { FakeRuntime } from "./fake-runtime";

export class MockSession {
  static async create(runtime: FakeRuntime): Promise<Session> {
    return connect({ runtime });
  }
}
