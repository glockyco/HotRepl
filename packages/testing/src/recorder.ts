import type { RuntimeRequest } from "@hotrepl/sdk";
import type { ServerMessage } from "@hotrepl/protocol";

export interface RecordedExchange {
  request: RuntimeRequest;
  response: ServerMessage;
}

export class SessionRecorder {
  readonly exchanges: RecordedExchange[] = [];

  record(request: RuntimeRequest, response: ServerMessage): void {
    this.exchanges.push({ request, response });
  }
}
