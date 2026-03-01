---
description: Narnia is an MCP server and Blazor web UI for browsing GitHub Copilot CLI session history. Find and resume sessions after a restart without losing context.
---

# Narnia

Narnia is an MCP server and local web UI for browsing your [GitHub Copilot CLI](https://githubnext.com/projects/copilot-in-the-cli) session history. It solves a real pain point: when Windows forces a restart (or any machine restarts), all active Copilot sessions disappear. Narnia lets you search, inspect, and resume sessions without losing context.

## MCP Tools

| Tool | Description |
|------|-------------|
| [`list_recent_sessions`](tools/list-recent-sessions.md) | List the most recently updated sessions |
| [`search_sessions`](tools/search-sessions.md) | Full-text search across session summaries, turns, and checkpoints |
| [`get_session_details`](tools/get-session-details.md) | Get full metadata and statistics for a session |
| [`get_session_checkpoints`](tools/get-session-checkpoints.md) | Retrieve all checkpoints with structured content |
| [`get_session_turns`](tools/get-session-turns.md) | Paginated conversation turn history |
| [`get_session_workspace`](tools/get-session-workspace.md) | Workspace metadata and session artifact files |
| [`list_sessions_by_repository`](tools/list-sessions-by-repository.md) | Filter sessions by git repository |
| [`list_sessions_by_cwd`](tools/list-sessions-by-cwd.md) | Filter sessions by working directory |
| [`open_narnia_ui`](tools/open-narnia-ui.md) | Start the Narnia web UI and open it in the browser |

## Why It Exists

Copilot CLI sessions accumulate rich context — checkpoints, conversation history, workspace artifacts, file change records. But a forced machine restart wipes all active sessions from memory. There was no built-in way to quickly answer "which session was I using for project X?" or "what was I doing yesterday in the macerus repo?"

Narnia indexes your local `~/.copilot/session-store.db` and session-state folder, making that history searchable and browsable — both from the terminal (via MCP) and from a local web interface.

## About

Built by [Nick Cosentino](https://www.devleader.ca) — Dev Leader. Nick writes about .NET, C#, software design, and developer tooling at [devleader.ca](https://www.devleader.ca).
