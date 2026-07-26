---
description: Narnia is an MCP server and Blazor web UI for browsing GitHub Copilot CLI session history. Find and resume sessions after a restart without losing context.
---

# Narnia

<p align="center">
  <img src="assets/narnia-logo.png" alt="Narnia logo" width="384">
</p>

Narnia is a single ASP.NET Core app — both an MCP server and a local web UI — for browsing your [GitHub Copilot CLI](https://githubnext.com/projects/copilot-in-the-cli) session history. It solves a real pain point: when Windows forces a restart (or any machine restarts), all active Copilot sessions disappear. Narnia lets you search, inspect, and resume sessions without losing context.

Use the star control in any session view to favorite important sessions. Favorite state is shared across the UI, can be filtered or sorted on the Sessions page, and has a dedicated **Favorites** page.

Table columns can be resized by dragging the separator at the right edge of a header or by focusing it and using the arrow keys. Widths are stored in the browser for each table; double-click a separator or press Escape to restore that column's default width.

The **Storage** page measures local session-state disk usage, growth, staleness, and cleanup safety.
The previous recorded-path search remains available as a secondary file-activity audit.
Session detail pages can recover incompatible history in place by retaining the same session ID and
folder while Copilot reseeds only the active event stream.

The supported prebuilt release is Windows x64. Narnia's defining recovery, scheduling, autostart,
and terminal-launch features integrate with Windows Terminal, WMI, and Windows Task Scheduler.

## MCP Tools

Narnia exposes 27 tools over one shared HTTP endpoint (`/mcp`): eight for session history, three for
broken-session recovery, four for local session storage, eight for managing scheduled jobs, and four
for file-based schedule portability.

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

### Session Storage

| Tool | Description |
|------|-------------|
| [`get_session_storage_overview`](tools/get-session-storage-overview.md) | Cached local storage totals and largest sessions |
| [`scan_session_storage`](tools/scan-session-storage.md) | Queue a metadata-only background scan |
| [`preview_local_session_cleanup`](tools/preview-local-session-cleanup.md) | Dry-run cleanup safety and reclaim estimates |
| [`delete_local_sessions`](tools/delete-local-sessions.md) | Delete validated local session data through Copilot SDK |

### Broken Session Recovery

| Tool | Description |
|------|-------------|
| [`preview_session_migration`](tools/preview-session-migration.md) | Inspect compatibility and recoverable context |
| [`migrate_broken_session`](tools/migrate-broken-session.md) | Reseed the same session ID and folder |
| [`get_session_recovery_packet`](tools/get-session-recovery-packet.md) | Read exact archived recovery context in chunks |

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
| [`export_schedule_package`](tools/export-schedule-package.md) | Export selected jobs for transfer or sharing |
| [`build_schedule_package`](tools/build-schedule-package.md) | Package selected non-Narnia task definitions |
| [`preview_schedule_package`](tools/preview-schedule-package.md) | Inspect destination bindings, dependencies, and conflicts |
| [`import_schedule_package`](tools/import-schedule-package.md) | Import an accepted preview as disabled jobs |

## Why It Exists

Copilot CLI sessions accumulate rich context — checkpoints, conversation history, workspace artifacts, file change records. But a forced machine restart wipes all active sessions from memory. There was no built-in way to quickly answer "which session was I using for project X?" or "what was I doing yesterday in the macerus repo?"

Narnia indexes your local `~/.copilot/session-store.db` and session-state folder, making that history searchable and browsable — both from the terminal (via MCP) and from a local web interface.

## About

Built by [Nick Cosentino](https://www.devleader.ca) — Dev Leader. Nick writes about .NET, C#, software design, and developer tooling at [devleader.ca](https://www.devleader.ca).
