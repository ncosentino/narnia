---
description: Preview recoverable history and safety state before reseeding a broken Copilot session in place.
---

# `preview_session_migration`

Inspects a source session without modifying it. The result includes resume-safety evidence,
recoverable turn/checkpoint/task counts, active-session blocking, existing migration state, and the
Narnia references that would carry forward.

| Parameter | Description |
|-----------|-------------|
| `sessionId` | Source Copilot session GUID |

Use this before `migrate_broken_session`.
