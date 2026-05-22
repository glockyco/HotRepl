import { connect } from "@hotrepl/sdk";
import type { ConnectOptions, RuntimeTransport, Session } from "@hotrepl/sdk";

export interface SessionManagerOptions {
  env?: Record<string, string | undefined>;
  onNotification?: (message: string) => void;
  runtime?: RuntimeTransport;
  url?: string;
}

export class SessionManager {
  private readonly options: SessionManagerOptions;
  private evicted = false;
  private notified = false;
  private session: Session | undefined;

  constructor(options: SessionManagerOptions = {}) {
    this.options = options;
  }

  async getSession(): Promise<Session> {
    if (this.session !== undefined && !this.evicted) return this.session;

    const connectOptions: ConnectOptions = {};
    if (this.options.env !== undefined) connectOptions.env = this.options.env;
    if (this.options.runtime !== undefined) connectOptions.runtime = this.options.runtime;
    if (this.options.url !== undefined) connectOptions.url = this.options.url;

    const session = await connect(connectOptions);
    this.session = session;
    this.evicted = false;
    this.notified = false;
    session.onSessionEvicted((event) => {
      if (this.session !== session || this.evicted) return;
      this.evicted = true;
      if (this.notified) return;
      this.notified = true;
      this.options.onNotification?.(`HotRepl session evicted: ${event.reason}`);
    });
    return session;
  }
}
