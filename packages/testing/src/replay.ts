import type { ServerMessage } from "@hotrepl/protocol";
import type { RuntimeRequest } from "@hotrepl/sdk";
import type { RecordedExchange } from "./recorder";

export class SessionReplay {
  private offset = 0;
  private readonly exchanges: readonly RecordedExchange[];

  constructor(exchanges: readonly RecordedExchange[]) {
    this.exchanges = exchanges;
  }

  next(request: RuntimeRequest): ServerMessage {
    const exchange = this.exchanges[this.offset];
    if (exchange === undefined) throw new Error("Replay is exhausted.");
    this.offset += 1;
    if (JSON.stringify(exchange.request) !== JSON.stringify(request)) {
      throw new Error("Replay request did not match recorded request.");
    }
    return exchange.response;
  }
}
