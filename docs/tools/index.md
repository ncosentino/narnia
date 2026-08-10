---
description: Reference for Narnia MCP tools covering session history, recovery, storage, and portable scheduled Copilot jobs.
---

# MCP Tools

Narnia exposes twenty-seven MCP tools: eight for session history, three for broken-session recovery,
four for local session storage, eight for scheduled-job management, and four for file-based schedule
portability. Use them from any AI assistant that supports the Model Context Protocol.

## Session History Tools

| Tool | Purpose | Key Parameters |
|------|---------|----------------|
| [`list_recent_sessions`](list-recent-sessions.md) | List most recently updated sessions | `limit` |
| [`search_sessions`](search-sessions.md) | Ranked session-content search | `query`, `limit` |
| [`get_session_details`](get-session-details.md) | Full metadata for one session | `sessionId` |
| [`get_session_checkpoints`](get-session-checkpoints.md) | All checkpoints for a session | `sessionId` |
| [`get_session_turns`](get-session-turns.md) | Paginated conversation turns | `sessionId`, `offset`, `limit` |
| [`get_session_workspace`](get-session-workspace.md) | Git root and artifact file list | `sessionId` |
| [`list_sessions_by_repository`](list-sessions-by-repository.md) | Exact effective remote-repository filter | `repo` |
| [`list_sessions_by_cwd`](list-sessions-by-cwd.md) | Exact working-directory filter | `cwd` |

!!! note "Looking for the web UI launcher?"
    The `open_narnia_ui` MCP tool has been replaced by the [`narnia-web-server` skill](../skills/narnia-web-server.md), which provides more reliable lifecycle management with full visibility into build output.

### Common Workflow

After a machine restart, choose the field you actually know:

1. `list_recent_sessions` — scan what was running recently
2. `list_sessions_by_repository` or `list_sessions_by_cwd` — filter exact metadata
3. `search_sessions` — search conversation content by topic
4. `get_session_details` — confirm the right session before resuming
5. `get_session_checkpoints` — read the last checkpoint to restore context

Session-history tools are read-only and return JSON.

## Broken Session Recovery Tools

Recovery tools archive the broken event stream and ask Copilot to reseed the same session ID and
folder. Narnia does not modify Chronicle.

| Tool | Purpose | Key Parameters |
|------|---------|----------------|
| [`preview_session_migration`](preview-session-migration.md) | Inspect compatibility and migration impact | `sessionId` |
| [`migrate_broken_session`](migrate-broken-session.md) | Reseed the same session ID and folder | `sessionId`, `confirmMigration` |
| [`get_session_recovery_packet`](get-session-recovery-packet.md) | Read archived recovery context in chunks | `sessionId`, `offset`, `maxCharacters` |

## Git Worktree Tools

| Tool | Purpose | Key Parameters |
|------|---------|----------------|
| [`get_session_worktrees`](get-session-worktrees.md) | List a session's worktrees and flag branch-override mismatches | `sessionId` |

## Session Storage Tools

Storage tools use the same cached scanner and cleanup service as the web UI.

| Tool | Purpose | Key Parameters |
|------|---------|----------------|
| [`get_session_storage_overview`](get-session-storage-overview.md) | Cached totals and largest local sessions | — |
| [`scan_session_storage`](scan-session-storage.md) | Queue a metadata-only background scan | — |
| [`preview_local_session_cleanup`](preview-local-session-cleanup.md) | Dry-run cleanup safety and reclaim estimates | `sessionIds`, `overrideProtections` |
| [`delete_local_sessions`](delete-local-sessions.md) | Delete validated local data through Copilot SDK | `sessionIds`, `overrideProtections`, `archiveDeletedSessions`, `confirmLocalDeletion` |

Deletion is local-only, irreversible, and requires explicit confirmation. Synced GitHub copies and
Narnia-owned metadata remain.

## Scheduled Job Tools

Manage and transfer Narnia-owned scheduled Copilot jobs — recurring `copilot -p` runs that Windows
Task Scheduler executes unattended. See the
[narnia-scheduler skill](../skills/narnia-scheduler.md) for create, migration, packaging, import,
verification, and handoff workflows.

| Tool | Purpose | Key Parameters |
|------|---------|----------------|
| [`list_schedules`](list-schedules.md) | All cataloged jobs joined to live task status | — |
| [`get_schedule`](get-schedule.md) | A single job's full catalog entry by id | `id` |
| [`get_schedule_log`](get-schedule-log.md) | Latest run log and live running state | `id` |
| [`create_schedule`](create-schedule.md) | Create a job and (by default) register its task | `name`, `prompt`, `cadenceKind`, `time` |
| [`update_schedule`](update-schedule.md) | Replace a job's definition and re-register | `id`, `name`, `prompt` |
| [`set_schedule_enabled`](set-schedule-enabled.md) | Enable/disable a job's task | `id`, `enabled` |
| [`run_schedule_now`](run-schedule-now.md) | Start a job's task immediately | `id` |
| [`delete_schedule`](delete-schedule.md) | Remove a job's task, wrapper, and catalog entry | `id` |
| [`export_schedule_package`](export-schedule-package.md) | Export selected Narnia jobs | `jobIds`, `profile` |
| [`build_schedule_package`](build-schedule-package.md) | Package definitions reconstructed from external tasks | `jobs`, `profile` |
| [`preview_schedule_package`](preview-schedule-package.md) | Resolve bindings and inspect destination readiness | `packageJson` |
| [`import_schedule_package`](import-schedule-package.md) | Import an accepted preview as disabled jobs | `packageJson`, `previewFingerprint` |

These tools are backed by the same service as the web UI's Schedules page — creating or editing a job through either surface is immediately visible in the other.

`list_schedules` reports a `health` per job. A scheduler exit code of `0` does not prove the run
finished its work, because the Copilot CLI exits cleanly when it is interrupted — see
[Scheduled Job Health](../schedule-health.md).

See [Portable Scheduled Jobs](../schedule-portability.md) for the complete transfer and handoff
workflow.
