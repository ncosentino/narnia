---
description: List all Copilot CLI sessions started in a specific working directory path. An alternative to repository-based filtering when the repo field is not set.
---

# list_sessions_by_cwd

Returns all sessions that were started in a specific working directory. This is the fallback when you don't know the repository name, or when the session was started outside of a git repo.

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

Path matching is exact — the `cwd` must match the stored value precisely, including casing on case-sensitive systems. On Windows, use backslashes (`C:\dev\myproject`) as that is how paths are stored. If no sessions match, try [`list_sessions_by_repository`](list-sessions-by-repository.md) or [`search_sessions`](search-sessions.md) with the project name instead.
