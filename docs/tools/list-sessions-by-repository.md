---
description: List all Copilot CLI sessions for a specific git repository in owner/repo format. Filter by project without searching manually.
---

# list_sessions_by_repository

Returns all sessions associated with a specific git repository. Useful when you know the repo name and want to see everything Copilot CLI has worked on in that project.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `repo` | string | Yes | Repository in `owner/repo` format, e.g. `ncosentino/needlr` |

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

The `repo` value must match the `repository` field stored in the session record exactly, in `owner/repo` format. If a session was created without a git repository context, it will not appear in this tool's results — use [`list_sessions_by_cwd`](list-sessions-by-cwd.md) as a fallback.
