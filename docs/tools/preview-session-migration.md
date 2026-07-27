---
description: Preview recoverable history and safety state before reseeding a broken Copilot session in place.
---

# `preview_session_migration`

Inspects a source session without modifying it. The result includes resume-safety evidence,
recoverable turn/checkpoint/task counts, active-session blocking, existing migration state, and the
Narnia references that would carry forward.

Known incompatible histories include invalid or missing `session.start` records and event streams
whose decoded content exceeds Copilot's current 536,870,888-character whole-file loader ceiling.
The oversized case otherwise looks structurally valid, but direct `--resume` can silently create an
unrelated blank session.

| Parameter | Description |
|-----------|-------------|
| `sessionId` | Source Copilot session GUID |

Use this before `migrate_broken_session`.
