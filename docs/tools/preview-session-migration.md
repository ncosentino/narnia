---
description: Preview recoverable history and safety state before reseeding a broken Copilot session in place.
---

# `preview_session_migration`

Inspects a source session without modifying it. The result includes resume-safety evidence,
recoverable turn/checkpoint/task counts, active-session blocking, existing migration state, and the
Narnia references that would carry forward. Recovery preparation scans the raw event stream with
bounded memory and compares recent direct user messages with selected Chronicle turns. This detects
an incomplete index without treating agent-generated steering as human direction; the read never
changes Copilot-owned files.

Known incompatible histories include invalid or missing `session.start` records. Narnia also keeps
a conservative compatibility guard for event streams whose decoded content exceeds the historical
536,870,888-character whole-file loader ceiling. If the installed Copilot package explicitly
advertises reliable very-large-session resume, native resume is allowed instead. The preview reports
`resumePolicy` and `copilotVersion` so callers can see which path was selected.

| Parameter | Description |
|-----------|-------------|
| `sessionId` | Source Copilot session GUID |

Use this before `migrate_broken_session`.
