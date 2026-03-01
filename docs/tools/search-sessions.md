---
description: Full-text search across Copilot CLI session summaries, conversation turns, and checkpoints using FTS5 syntax. Find sessions by topic, project name, or any keyword.
---

# search_sessions

Full-text search across all session content stored in `session-store.db`: session summaries, conversation turn messages, and checkpoint text. Uses SQLite FTS5 for fast ranked matching.

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `query` | string | Yes | — | Search query. Supports FTS5 syntax — see notes below. |
| `limit` | integer | No | `10` | Maximum number of results to return |

## FTS5 Query Syntax

| Syntax | Example | Matches |
|--------|---------|---------|
| Simple term | `authentication` | Pages containing "authentication" |
| Prefix match | `auth*` | "auth", "authentication", "authorize", … |
| Phrase match | `"dependency injection"` | Exact phrase |
| AND (implicit) | `blazor server` | Both words present |
| OR | `blazor OR razor` | Either word |
| NOT | `blazor NOT server` | "blazor" without "server" |

## Response

Returns a JSON array of search result objects:

```json
[
  {
    "sessionId": "0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4",
    "sourceType": "turn",
    "sourceId": "42",
    "content": "...the dependency injection container needs to register...",
    "score": 0.87
  }
]
```

`sourceType` values: `"turn"`, `"checkpoint_overview"`, `"checkpoint_history"`, `"checkpoint_work_done"`, `"checkpoint_technical"`, `"checkpoint_files"`, `"checkpoint_next_steps"`, `"workspace_artifact"`

## Example Prompts

- "Search my sessions for 'dependency injection'"
- "Find sessions where I was working on authentication"
- "Search for 'macerus RPG' across all my sessions"
- "Find sessions that mention 'NativeAOT' or 'trimming'"

## Notes

Results are ranked by relevance score. Multiple results from the same session can appear. Use the `sessionId` from a result to call [`get_session_details`](get-session-details.md) for the full session context.
