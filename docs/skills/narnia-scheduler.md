# narnia-scheduler

Create, migrate, and manage Narnia-owned scheduled Copilot jobs — recurring `copilot -p` runs that Windows Task Scheduler executes unattended on a daily, weekly, or monthly cadence.

This skill is a thin workflow layer over the [scheduled-job MCP tools](../tools/index.md#scheduled-job-tools) (`list_schedules`, `get_schedule`, `create_schedule`, `update_schedule`, `set_schedule_enabled`, `run_schedule_now`, `delete_schedule`), which are already available as regular tool calls — the skill adds the judgment: designing a self-contained prompt, migrating an existing hand-made scheduled task, and verifying a job before trusting it.

## Capabilities

| Flow | Description |
|------|-------------|
| **Create from scratch** | Design a prompt + cadence, call `create_schedule`, verify the task registered |
| **Migrate an existing task** | Introspect a hand-made Windows Scheduled Task, translate its trigger to a Narnia cadence, and register an equivalent self-contained job |
| **Supervised dry run** | Run a job's generation logic manually (optionally with secrets scrubbed from the environment) before trusting it on an unattended schedule |

## How It Works

### Creating a job

1. Design a self-contained prompt — one that resolves its own secrets (e.g. from a repo `.env`) rather than assuming any environment variable is pre-set, since the job runs as a plain `copilot -p` with no injected environment.
2. Pick a cadence (`daily` / `weekly` / `monthly`) and call `create_schedule`.
3. Verify with `get_schedule` — confirm `taskFound: true` and a sane `nextRunTime`.

### Migrating an existing task

1. Run the bundled `Read-ExistingScheduledTask.ps1` script against the task's name/folder. It reads the task's exported XML (not the `ScheduledTasks` module's CIM `.Triggers`, which is unreliable for calendar triggers registered via raw XML) and returns a suggested Narnia cadence per trigger.
2. Read the task's underlying script yourself to understand what it actually does.
3. Design an equivalent self-contained prompt and call `create_schedule`.
4. **Disable** (never delete) the original task once the new job is verified.

### Supervised dry run

The bundled `Invoke-NarniaDryRun.ps1` script runs a prompt via `copilot -p` from a given directory, optionally scrubbing environment variables matching a given prefix first — proving secret self-resolution actually works rather than relying on variables your interactive shell happens to already have set.

## Prerequisites

- Windows (Task Scheduler). `create_schedule`/`update_schedule` support a `register: false` copy-paste mode on unsupported platforms.
- The Narnia web server running (shared MCP endpoint) — see the [narnia-web-server skill](narnia-web-server.md).

## Usage Examples

Just ask the LLM naturally:

- *"Schedule a daily job at 5am that runs my example-radar skill"*
- *"Migrate my hand-made 'Nightly Backup' scheduled task into Narnia"*
- *"List my scheduled jobs and tell me if any are failing"*
- *"Disable my example radar job for now"*

## Design Principles

- **Narnia is a metadata registry + wrapper generator, not a scheduler.** Windows Task Scheduler remains the executor.
- **One format.** Every job is first-class and always editable — there is no separate "adopted" tier.
- **No pre-injected environment.** A job's prompt/skill must self-resolve its own secrets.
- **Orchestration lives in the prompt, never in Narnia.** Multi-step behavior is prompt text or a script colocated with the skill it belongs to.
