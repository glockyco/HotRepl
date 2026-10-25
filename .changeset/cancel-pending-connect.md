---
"@hotrepl/sdk": minor
---

Add an optional `signal` to `connect` options to cancel connection establishment and close pending
transports. Connection failures and protocol mismatches now close their transports, and sockets
closed before the handshake reject instead of leaving connection promises pending.
