---
description: List the most recently updated GitHub Copilot CLI sessions. The fastest way to find a session to resume after a restart.
---

# list_recent_sessions

Returns the most recently updated Copilot CLI sessions from your local `session-store.db`. This is the fastest starting point after a machine restart when you need to find which session to resume.

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `limit` | integer | No | `20` | Maximum number of sessions to return |

## Response

Returns a JSON array of session summary objects:

```json
[
  {
    "id": "0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4",
    "cwd": "C:\\dev\\nexus-labs\\needlr",
    "repository": "nexus-labs/needlr",
    "branch": "main",
    "summary": "Explore Microsoft Agent Framework Integration",
    "createdAt": "2026-02-15T10:30:00Z",
    "updatedAt": "2026-02-28T18:45:00Z",
    "turnCount": 87,
    "checkpointCount": 66
  }
]
```

## Example Prompts

- "List my most recent Copilot sessions"
- "What sessions was I working on recently? Show the last 5."
- "Which session was I using yesterday? Show me the 10 most recent."
- "Find my most recent sessions and tell me which ones have the most turns"

## Notes

Results are ordered by `updatedAt` descending — the most recently active session appears first. Use [`get_session_checkpoints`](get-session-checkpoints.md) after identifying a session to restore context from the last checkpoint.
