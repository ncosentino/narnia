---
description: List visible Copilot CLI sessions for an exact remote repository in owner/repository format, after applying Narnia overrides.
---

# list_sessions_by_repository

Returns visible sessions whose effective remote repository matches a specific `owner/repository` value. Narnia applies repository overrides before filtering, so corrected metadata is used consistently.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `repo` | string | Yes | Remote repository in `owner/repository` format, e.g. `ncosentino/needlr` |

## Response

Returns a JSON array of session summary objects (same structure as [`list_recent_sessions`](list-recent-sessions.md)), ordered by `updatedAt` descending:

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

- "List all sessions for the ncosentino/needlr repository"
- "What sessions have I worked on in the macerus repo? Use ncosentino/macerus."
- "Find all sessions for ncosentino/narnia"
- "Which sessions were working on my devleader-blog? Try ncosentino/devleader-blog."

## Notes

Matching is exact but case-insensitive. Archived sessions are excluded, and Narnia repository overrides replace incorrect Chronicle values before filtering. A session without a remote repository will not appear — use [`list_sessions_by_cwd`](list-sessions-by-cwd.md) when the working directory is the reliable identifier.
