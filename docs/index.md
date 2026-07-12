---
description: Narnia is an MCP server and Blazor web UI for browsing GitHub Copilot CLI session history. Find and resume sessions after a restart without losing context.
---

# Narnia

Narnia is a single ASP.NET Core app — both an MCP server and a local web UI — for browsing your [GitHub Copilot CLI](https://githubnext.com/projects/copilot-in-the-cli) session history. It solves a real pain point: when Windows forces a restart (or any machine restarts), all active Copilot sessions disappear. Narnia lets you search, inspect, and resume sessions without losing context.

Use the star control in any session view to favorite important sessions. Favorite state is shared across the UI, can be filtered or sorted on the Sessions page, and has a dedicated **Favorites** page.

Table columns can be resized by dragging the separator at the right edge of a header or by focusing it and using the arrow keys. Widths are stored in the browser for each table; double-click a separator or press Escape to restore that column's default width.

The supported prebuilt release is Windows x64. Narnia's defining recovery, scheduling, autostart,
and terminal-launch features integrate with Windows Terminal, WMI, and Windows Task Scheduler.

## MCP Tools

Narnia exposes 16 tools over one shared HTTP endpoint (`/mcp`): eight for session history, eight for managing scheduled Copilot jobs.

### Session History

| Tool | Description |
|------|-------------|
| [`list_recent_sessions`](tools/list-recent-sessions.md) | List the most recently updated sessions |
| [`search_sessions`](tools/search-sessions.md) | Ranked full-text search across session content |
| [`get_session_details`](tools/get-session-details.md) | Get full metadata and statistics for a session |
| [`get_session_checkpoints`](tools/get-session-checkpoints.md) | Retrieve all checkpoints with structured content |
| [`get_session_turns`](tools/get-session-turns.md) | Paginated conversation turn history |
| [`get_session_workspace`](tools/get-session-workspace.md) | Workspace metadata and session artifact files |
| [`list_sessions_by_repository`](tools/list-sessions-by-repository.md) | Filter by exact effective remote repository |
| [`list_sessions_by_cwd`](tools/list-sessions-by-cwd.md) | Filter by exact working directory |

### Scheduled Jobs

| Tool | Description |
|------|-------------|
| [`list_schedules`](tools/list-schedules.md) | All cataloged scheduled jobs joined to live task status |
| [`get_schedule`](tools/get-schedule.md) | A single scheduled job's full catalog entry by id |
| [`get_schedule_log`](tools/get-schedule-log.md) | Read the latest run log and whether the task is still running |
| [`create_schedule`](tools/create-schedule.md) | Create a scheduled job and (by default) register its task |
| [`update_schedule`](tools/update-schedule.md) | Replace a scheduled job's definition and re-register it |
| [`set_schedule_enabled`](tools/set-schedule-enabled.md) | Enable/disable a scheduled job's task |
| [`run_schedule_now`](tools/run-schedule-now.md) | Start a scheduled job's task immediately |
| [`delete_schedule`](tools/delete-schedule.md) | Remove a scheduled job's task, wrapper, and catalog entry |

## Why It Exists

Copilot CLI sessions accumulate rich context — checkpoints, conversation history, workspace artifacts, file change records. But a forced machine restart wipes all active sessions from memory. There was no built-in way to quickly answer "which session was I using for project X?" or "what was I doing yesterday in the macerus repo?"

Narnia indexes your local `~/.copilot/session-store.db` and session-state folder, making that history searchable and browsable — both from the terminal (via MCP) and from a local web interface.

## About

Built by [Nick Cosentino](https://www.devleader.ca) — Dev Leader. Nick writes about .NET, C#, software design, and developer tooling at [devleader.ca](https://www.devleader.ca).
