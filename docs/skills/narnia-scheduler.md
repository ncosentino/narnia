# narnia-scheduler

Create, migrate, move, share, and manage Narnia-owned scheduled Copilot jobs.

The skill is the judgment layer over Narnia's scheduled-job and
[package tools](../tools/index.md#scheduled-job-tools). It designs self-contained prompts, inspects
selected Windows tasks, resolves portability requirements, and coordinates a safe disabled-first
handoff.

## Capabilities

| Flow | Description |
|------|-------------|
| **Create from scratch** | Design a prompt + cadence, call `create_schedule`, verify the task registered |
| **Migrate an existing task** | Introspect a hand-made Windows Scheduled Task, translate its trigger to a Narnia cadence, and register an equivalent self-contained job |
| **Export/share Narnia jobs** | Create a transfer package or sanitized share template |
| **Package external tasks** | Reconstruct selected non-Narnia task behavior without adopting it locally |
| **Import and hand off** | Resolve bindings, import disabled, dry-run, enable the destination, then disable the source |
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

### Moving or sharing jobs

Use `export_schedule_package` for existing Narnia jobs. Use the read-only
`Find-CopilotScheduledTasks.ps1` plus `Read-ExistingScheduledTask.ps1` and
`build_schedule_package` for selected external tasks.

On the destination, call `preview_schedule_package` until paths, task names, and dependencies are
resolved, then call `import_schedule_package`. Imported jobs are registered disabled. Save the
receipt, run a supervised test, enable the destination, and only then disable the original source
task.

See [Portable Scheduled Jobs](../schedule-portability.md).

## Prerequisites

- Windows (Task Scheduler). `create_schedule`/`update_schedule` support a `register: false` copy-paste mode on unsupported platforms.
- The Narnia web server running (shared MCP endpoint) — see the [narnia-web-server skill](narnia-web-server.md).

## Usage Examples

Just ask the LLM naturally:

- *"Schedule a daily job at 5am that runs my example-radar skill"*
- *"Migrate my hand-made 'Nightly Backup' scheduled task into Narnia"*
- *"List my scheduled jobs and tell me if any are failing"*
- *"Disable my example radar job for now"*
- *"Export these schedules so I can move them to another computer"*
- *"Package this existing Windows task without registering it in Narnia"*
- *"Preview and import this .narnia-schedules.json file"*
- *"Schedule my weekly report and explicitly deliver the Markdown with the [narnia-report-email skill](narnia-report-email.md)"*

## Design Principles

- **Narnia is a metadata registry + wrapper generator, not a scheduler.** Windows Task Scheduler remains the executor.
- **One format.** Every job is first-class and always editable — there is no separate "adopted" tier.
- **No pre-injected environment.** A job's prompt/skill must self-resolve its own secrets.
- **Orchestration lives in the prompt, never in Narnia.** Multi-step behavior is prompt text or a script colocated with the skill it belongs to.
- **Report delivery stays explicit.** A prompt may invoke `narnia-report-email` after generation, but the scheduler never injects SMTP behavior.
- **Packages never contain secrets or generated machine artifacts.**
- **Imports are disabled-first.** Nothing runs until the destination is explicitly enabled.
