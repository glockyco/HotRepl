---
"@hotrepl/cli": minor
---

Make `hotrepl wait` retry unavailable hosts until its deadline, configurable with `--timeout-ms`,
and return the timeout exit code when the host does not answer. Stream `watch` ticks immediately in
text, JSON, and JSONL output while preserving output and cleanup on interruption or failure.
