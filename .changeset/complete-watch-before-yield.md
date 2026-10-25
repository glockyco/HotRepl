---
"@hotrepl/sdk": patch
---

Stop sending redundant cancellation frames after a watch completes with a final value or error.
Watches interrupted before a final frame still cancel their active subscription.
