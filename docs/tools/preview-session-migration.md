---
description: Preview recoverable history and safety state before reseeding a broken Copilot session in place.
---

# `preview_session_migration`

Inspects a source session without modifying it. The result includes resume-safety evidence,
recoverable turn/checkpoint/task counts, active-session blocking, existing migration state, and the
Narnia references that would carry forward. Recovery preparation also reads a bounded tail of the
raw event stream so Narnia can detect when the Chronicle index may be behind the local stream; this
read is diagnostic only and never changes Copilot-owned files.

Known incompatible histories include invalid or missing `session.start` records. Narnia also keeps
a conservative compatibility guard for event streams whose decoded content exceeds the historical
536,870,888-character whole-file loader ceiling. Newer Copilot capability handling is tracked
separately; the preview reports the evidence used rather than assuming every runtime behaves alike.

| Parameter | Description |
|-----------|-------------|
| `sessionId` | Source Copilot session GUID |

Use this before `migrate_broken_session`.
