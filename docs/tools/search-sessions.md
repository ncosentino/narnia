---
description: Ranked full-text search across Copilot CLI session content. Use exact repository and working-directory tools for metadata filtering.
---

# search_sessions

Full-text search across indexed session content: summaries, conversation turns, checkpoints, and workspace artifacts. Uses SQLite FTS5 for ranked matching, returning the strongest matching row from each visible session. Archived sessions are excluded.

This is a content search, not a Remote Repository or Working Directory filter. Use [`list_sessions_by_repository`](list-sessions-by-repository.md) or [`list_sessions_by_cwd`](list-sessions-by-cwd.md) when you need exact metadata matching.

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `query` | string | Yes | — | Content query. Supports words, implicit AND, `OR`, `NOT`, and `*` prefix matching. |
| `limit` | integer | No | `10` | Maximum number of results to return |

## FTS5 Query Syntax

| Syntax | Example | Matches |
|--------|---------|---------|
| Simple term | `authentication` | Pages containing "authentication" |
| Prefix match | `auth*` | "auth", "authentication", "authorize", … |
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

Results are ranked by relevance, with at most one result per session. Use the `sessionId` from a result to call [`get_session_details`](get-session-details.md) for the full session context.
