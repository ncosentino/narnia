---
description: Overview of all eight Narnia MCP tools for searching and browsing GitHub Copilot CLI session history. Use these from any AI assistant that supports MCP.
---

---
description: Reference for all eight Narnia MCP tools. Search sessions, browse checkpoints, and read conversation history from any MCP-enabled AI assistant.
---

# MCP Tools

Narnia exposes eight MCP tools for working with your local Copilot CLI session history. Use them from any AI assistant that supports the Model Context Protocol — including GitHub Copilot CLI, Claude Desktop, Cursor, and VS Code.

## Tool Reference

| Tool | Purpose | Key Parameters |
|------|---------|----------------|
| [`list_recent_sessions`](list-recent-sessions.md) | List most recently updated sessions | `limit` |
| [`search_sessions`](search-sessions.md) | FTS5 full-text search | `query`, `limit` |
| [`get_session_details`](get-session-details.md) | Full metadata for one session | `sessionId` |
| [`get_session_checkpoints`](get-session-checkpoints.md) | All checkpoints for a session | `sessionId` |
| [`get_session_turns`](get-session-turns.md) | Paginated conversation turns | `sessionId`, `offset`, `limit` |
| [`get_session_workspace`](get-session-workspace.md) | Git root and artifact file list | `sessionId` |
| [`list_sessions_by_repository`](list-sessions-by-repository.md) | Filter by git repository | `repo` |
| [`list_sessions_by_cwd`](list-sessions-by-cwd.md) | Filter by working directory | `cwd` |

!!! note "Looking for the web UI launcher?"
    The `open_narnia_ui` MCP tool has been replaced by the [`narnia-web-server` skill](../skills/narnia-web-server.md), which provides more reliable lifecycle management with full visibility into build output.

## Common Workflow

After a machine restart, start by listing recent sessions or searching by project name:

1. `list_recent_sessions` — scan what was running recently
2. `search_sessions` — narrow down by keyword or repo name
3. `get_session_details` — confirm the right session before resuming
4. `get_session_checkpoints` — read the last checkpoint to restore context

All tools return JSON. Results are read-only — Narnia never modifies your session data.
