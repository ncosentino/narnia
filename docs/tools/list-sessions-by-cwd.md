---
description: List visible Copilot CLI sessions started in an exact working directory. Use this separately from remote-repository filtering.
---

# list_sessions_by_cwd

Returns visible sessions that were started in a specific working directory. Working Directory is local filesystem metadata and is independent of the session's Remote Repository value.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `cwd` | string | Yes | Working directory path, e.g. `C:\dev\myproject` |

## Response

Returns a JSON array of session summary objects (same structure as [`list_recent_sessions`](list-recent-sessions.md)), ordered by `updatedAt` descending:

```json
[
  {
    "id": "31781275-f051-49b4-a46c-7385ffcab8c4",
    "cwd": "C:\\dev\\macerus",
    "repository": "ncosentino/macerus",
    "branch": "main",
    "summary": "Design Macerus RPG Framework",
    "createdAt": "2026-02-01T09:00:00Z",
    "updatedAt": "2026-03-01T17:00:00Z",
    "turnCount": 12,
    "checkpointCount": 7
  }
]
```

## Example Prompts

- "List sessions from C:\\dev\\macerus"
- "Show sessions that were started in my narnia project directory"
- "Find sessions in C:\\dev\\nexus-labs\\needlr"

## Notes

Path matching follows the operating system's casing rules and ignores one trailing directory separator. On Windows, use the stored backslash form (`C:\dev\myproject`); forward slashes are not normalized. Archived sessions are excluded. If no sessions match, inspect recent session metadata or use [`list_sessions_by_repository`](list-sessions-by-repository.md) with the exact remote repository.
