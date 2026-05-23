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
  private pendingConnect: Promise<Session> | undefined;

  constructor(options: SessionManagerOptions = {}) {
    this.options = options;
  }

  async getSession(): Promise<Session> {
    if (this.session !== undefined && !this.evicted) return this.session;
    // Dedupe concurrent callers (e.g. refreshAnnotations racing with a
    // tools/call handler). Without this, each caller awaits its own
    // connect() and we open N WebSockets — which a single-client backend
    // like BepInEx rejects with `session_evicted: displaced`.
    if (this.pendingConnect !== undefined) return this.pendingConnect;

    const connectOptions: ConnectOptions = {};
    if (this.options.env !== undefined) connectOptions.env = this.options.env;
    if (this.options.runtime !== undefined) connectOptions.runtime = this.options.runtime;
    if (this.options.url !== undefined) connectOptions.url = this.options.url;

    this.pendingConnect = (async () => {
      try {
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
      } finally {
        this.pendingConnect = undefined;
      }
    })();
    return this.pendingConnect;
  }

  /**
   * Close the cached session, if any. After close the SessionManager can
   * still be used: a subsequent getSession() will re-connect.
   *
   * Safe to call multiple times.
   */
  close(): void {
    if (this.session === undefined) return;
    this.session.close();
    this.session = undefined;
  }
}
