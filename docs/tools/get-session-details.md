---
description: Get full metadata and statistics for a specific Copilot CLI session by its GUID. Includes repository, branch, summary, turn count, and checkpoint count.
---

# get_session_details

Returns complete metadata and statistics for a single Copilot CLI session. Use this after finding a session ID via [`list_recent_sessions`](list-recent-sessions.md) or [`search_sessions`](search-sessions.md) to confirm it's the right one before restoring context.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sessionId` | string | Yes | The session GUID, e.g. `0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4` |

## Response

Returns a JSON session object:

```json
{
  "id": "0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4",
  "cwd": "C:\\dev\\nexus-labs\\needlr",
  "repository": "nexus-labs/needlr",
  "branch": "main",
  "summary": "Explore Microsoft Agent Framework Integration",
  "gitRoot": "C:\\dev\\nexus-labs\\needlr",
  "createdAt": "2026-02-15T10:30:00Z",
  "updatedAt": "2026-02-28T18:45:00Z",
  "turnCount": 87,
  "checkpointCount": 66
}
```

## Example Prompts

- "Get details for session 0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4"
- "What was session abc123 working on? Show me the full details."
- "Tell me about this session: [session ID from search results]"

## Notes

If the session ID is not found, the tool returns a plain error message. GUIDs must be the full 36-character form (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`).
