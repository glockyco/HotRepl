import type { ServerMessage } from "@hotrepl/protocol";
import type { RuntimeRequest } from "@hotrepl/sdk";

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
