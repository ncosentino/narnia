---
name: narnia-scheduler
description: >
  Create, migrate, and manage Narnia-owned scheduled Copilot jobs — recurring `copilot -p` runs
  that Windows Task Scheduler executes unattended on a daily/weekly/monthly cadence. Use to
  schedule a new recurring job, migrate an existing hand-built Windows Scheduled Task into Narnia,
  run a supervised dry-run before trusting a schedule, or inspect/enable/disable/run/delete a
  cataloged job.
license: MIT
compatibility: Windows only — depends on the ScheduledTasks PowerShell module and Narnia's MCP schedule tools being available.
metadata:
  author: nexus-labs
  version: "1.0"
allowed-tools: PowerShell(*) Read Write
---

# Narnia Scheduler

Create, migrate, and manage Narnia-owned scheduled Copilot jobs — recurring `copilot -p` runs that
Windows Task Scheduler executes unattended on a daily/weekly/monthly cadence. This skill is the
workflow layer on top of Narnia's schedule MCP tools, which are already available as regular tool
calls in any session sharing Narnia's MCP endpoint — there is no HTTP API or script to shell out to
for the core create/read/update/delete work.

## When to Use

- User asks to schedule a recurring Copilot task ("run this every morning", "email me a weekly report").
- User asks to migrate an existing Windows Scheduled Task they set up by hand into Narnia.
- User asks to list, inspect, enable/disable, run-now, or delete a scheduled job.

## Design invariants (read before acting)

- **Narnia is a metadata registry + wrapper generator, not a scheduler.** Windows Task Scheduler
  remains the executor. `create_schedule`/`update_schedule` generate a self-contained wrapper script
  under Narnia's own app-data folder and register/refresh the OS task; they never touch a user's own
  scripts.
- **One format — every job is first-class.** There is no "pointer/adopted" job that isn't editable.
  Every job created through `create_schedule` owns its generated wrapper and can always be edited
  later with `update_schedule`. Never invent a second tier of jobs that aren't fully editable.
- **The job runs as a plain `copilot -p <prompt>` with NO pre-injected environment.** Whatever a
  prompt/skill needs (database connections, API keys, SMTP credentials, ...) must be self-resolved by
  the skill/script itself (e.g. read a repo `.env`) — never assume a wrapper will inject it. See
  *Writing a self-contained prompt* below.
- **No nested `copilot`.** The wrapper already IS the `copilot -p` invocation. The prompt should
  never itself try to spawn another `copilot` call; deterministic follow-up work (a database write,
  an email, a git commit) belongs in a plain script that the prompt calls directly.
- **Orchestration lives in the prompt or the skill it invokes — never in Narnia.** If a job needs
  multi-step behavior (generate → validate → write → notify), that sequencing is prompt text or a
  script colocated with the skill, not a new Narnia feature. Narnia only stores metadata and
  generates the wrapper; it does not understand what a job's prompt does.
- **Prefer disabling over deleting when migrating.** Keep the original scheduled task as a disabled
  backup until the Narnia-owned replacement has proven itself on at least one real run.

## The MCP tools (primitives — call these directly, no scripts needed)

| Tool | Purpose |
|------|---------|
| `list_schedules` | All cataloged jobs joined to live Task Scheduler status, plus untracked `\Narnia\` tasks |
| `get_schedule` | A single job's full catalog entry (including its prompt) by id |
| `create_schedule` | Create a job: generate the wrapper, and (by default) register the OS task |
| `update_schedule` | Replace a job's definition, regenerate its wrapper, re-register in place |
| `set_schedule_enabled` | Enable/disable a job's OS task without deleting it |
| `run_schedule_now` | Start a job's OS task immediately, out of band from its cadence |
| `delete_schedule` | Remove a job's OS task, wrapper, and catalog entry (irreversible) |

These are already available as normal tool calls — do not shell out to an HTTP API or the web UI to
do what these tools do directly.

## Flow A: create a new job from scratch

1. **Design the prompt first** (see checklist below) — this is the actual job; get it right before
   worrying about cadence.
2. **Pick a cadence**: `cadenceKind` = `daily` | `weekly` | `monthly`, a 24-hour `time`, and either
   `days` (weekly, day names) or `dayOfMonth` (monthly, 1-31).
3. **Call `create_schedule`** with `register: true` (the default) so the OS task is created now. Use
   `register: false` only if the user wants to review the generated script before installing it, or
   is on a platform where registration isn't supported — you'll get back `script` + `command` to hand
   to the user instead.
4. **Verify**: call `get_schedule` (or `list_schedules`) and confirm `taskFound: true`, a sane
   `nextRunTime`, and `state: "ready"`.
5. **Offer a supervised dry run** (see below) before considering the job trustworthy, especially if
   the prompt does anything side-effecting (writes, emails, commits).

## Flow B: migrate an existing Windows Scheduled Task

1. **Introspect the existing task** with the bundled script (reads the OS scheduler directly — this
   is NOT something the MCP tools can see, since they only know about Narnia-cataloged/`\Narnia\`
   tasks):
   ```powershell
   & "<skill-dir>/scripts/Read-ExistingScheduledTask.ps1" -TaskName "<name>" -TaskPath "<folder>"
   ```
   This returns one object per trigger with a `SuggestedCadence` (cadenceKind/time/days/dayOfMonth/cwd)
   ready to hand to `create_schedule`, plus `ResolvedScriptPath` if the task's action runs a script.
   - **A task with multiple triggers becomes multiple Narnia jobs** — one `create_schedule` call per
     supported trigger (a job has exactly one cadence). This mirrors how a task with separate Weekly
     and Monthly triggers should become two Narnia jobs, not one.
   - **`TriggerSupported: false`** means the trigger has no Narnia equivalent (a one-off `Once`
     trigger, or a boot/logon/idle/event trigger) — tell the user this one can't be migrated as-is.
2. **Read the resolved script yourself** (with your normal file tools) to understand what the task
   actually does: which skill it invokes, what prompt it builds, and — critically — whether it relies
   on a wrapper pre-injecting environment variables that a plain `copilot -p` job will NOT have.
3. **Design an equivalent, self-contained prompt** (see checklist below) that reproduces the same
   work without depending on the old wrapper's setup.
4. **Call `create_schedule`** with the suggested cadence and your new prompt (`register: true`).
5. **Verify** the new job (`get_schedule`/`list_schedules`) and, ideally, run a **supervised dry run**
   (below) before trusting it.
6. **Disable, never delete, the original task**:
   ```powershell
   Disable-ScheduledTask -TaskName "<name>" -TaskPath "<folder>"
   ```
   Keep it as a backup until the new job has completed at least one real unattended run.

## Writing a self-contained prompt (checklist)

A job's prompt runs under a plain `copilot -p` with no pre-injected environment. Before calling
`create_schedule`, check:

- [ ] **Secrets self-resolve.** If the skill/script the prompt invokes needs a secret (DB connection,
      API key, SMTP password), it reads it itself — e.g. from a repo `.env` — rather than assuming an
      environment variable is already set. A proven pattern: read the file, split each line on the
      first `=`, and strip **one matching pair** of surrounding quotes (single OR double — code that
      only strips double quotes will silently keep a single-quoted secret quoted and fail to connect;
      this is a real bug that has shipped before). Only set a variable if it isn't already set, so an
      explicit environment always wins.
- [ ] **No nested `copilot`.** The prompt should never itself invoke `copilot -p` — it's already
      running inside one. Deterministic follow-up (a DB write, an email, a git commit) is a plain
      script the prompt calls directly, colocated with the skill it belongs to.
- [ ] **Working directory is set** (`cwd`) whenever the prompt depends on a repo-local skill (a skill
      only available because Copilot was launched from that repo's directory) — check whether the
      skill is a globally-installed plugin or repo-local, and set `cwd` accordingly for the latter.
- [ ] **The prompt says exactly what to do with output**, not just "generate X" — e.g. "write the
      result to `./drafts/`, then run `scriptname.ps1 -DraftPath <path>` for the deterministic
      follow-up." There is no hidden wrapper behavior beyond what the prompt says.

## Supervised dry run (recommended before trusting any new or migrated job)

Before letting a new schedule run unattended for the first time, run its generation logic manually
with the bundled dry-run helper, ideally with the real secrets scrubbed from the environment so the
run proves self-resolution actually works (not that your current shell happens to already have the
right variables set):

```powershell
& "<skill-dir>/scripts/Invoke-NarniaDryRun.ps1" `
  -Cwd "C:\dev\example-repo" `
  -ScrubEnvPrefix "EXAMPLE_" `
  -Prompt "Generate ... using the example skill. Do NOT write to the database, do NOT send email, do NOT git commit or push -- produce only the local draft file and print its absolute path as the final line."
```

This only proves the *generation* half is self-contained. If the job also runs deterministic
follow-up (DB write, email), test that separately, with the write/email steps guarded behind an
explicit flag the way the original design intended (default off; the scheduled invocation turns it
on deliberately) rather than trying to make the dry-run script guess what's safe.

## Common mistakes (learned the hard way)

- **Assuming any environment is pre-injected.** There is no wrapper setting variables before your
  prompt runs — self-resolve or the job will fail its first real, unattended run in ways that never
  showed up when you tested from your own already-configured shell.
- **Trusting a `.Trim('"')`-only quote-strip pattern.** If a secret value in a `.env` file is
  single-quoted, stripping only double quotes leaves the value still quoted and breaks the
  connection string. Strip a matching pair of either quote character.
- **Relying on `.Triggers` CIM data for a migrated task's monthly schedule.** A calendar trigger
  registered via raw task XML (as Narnia itself does for monthly, since `New-ScheduledTaskTrigger` has
  no monthly option) can read back as a generic, detail-less trigger through the `ScheduledTasks`
  module's CIM collection. `Read-ExistingScheduledTask.ps1` reads the exported XML instead for exactly
  this reason — trust its output over a manual `Get-ScheduledTask .Triggers` inspection.
- **Deleting the original task during a migration.** Disable it instead; only delete once the new job
  has proven itself with a real run.

## Important notes

- **Windows only.** `create_schedule`/`update_schedule` with `register: true` require Windows Task
  Scheduler. `list_schedules`/`get_schedule` report `schedulerSupported: false` on unsupported
  platforms; use `register: false` to get a copy-paste script + command instead.
- **A job's prompt is its entire behavior.** Narnia never edits a user's own scripts and has no
  hidden orchestration beyond generating the wrapper and registering the task — everything the job
  does is exactly what its prompt says.
