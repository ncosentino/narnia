---
description: Move or share Narnia scheduled Copilot jobs between computers with versioned files instead of shared server state.
---

# Portable Scheduled Jobs

Narnia can export scheduled Copilot work to a versioned `.narnia-schedules.json` file and import it
on another computer. There is no Narnia cloud account or synchronization server: copy the package by
whatever transport you already trust.

The package contains the job definition, not the source machine's execution artifacts. The
destination generates new local Narnia IDs, wrapper scripts, log folders, and Windows Task Scheduler
entries.

## Transfer vs. share

| Profile | Use it when | Source information |
|---------|-------------|--------------------|
| **Transfer** | Moving work between computers you control | Retains non-secret path hints, source task identity, and enabled state |
| **Share** | Sending a reusable template to another person | Removes source-local paths and source job identity |

Both profiles use the same schema and importer.

## What a package contains

- Prompt template and description
- Daily, weekly, or monthly cadence
- Copilot allow flags and additional arguments
- Ordered plugin/repo-local skill requirements
- Path or repository bindings that must be resolved on the destination
- Source timezone
- Definition fingerprints that detect hand-edited or stale packages

## What is never included

- Narnia `settings.db`
- Copilot databases or session history
- Task Scheduler XML
- Generated `run.ps1` or `run.vbs`
- Run logs
- Credentials, tokens, `.env` contents, SMTP values, or database passwords
- Plugin source, repository clones, caches, or databases

Definition transfer is not application-state transfer. A job that depends on durable checkpoints,
caches, or another database may require a separate, explicit migration.

Package creation rejects definitions containing obvious credential-like literals. This check is
deliberately conservative rather than a complete secret scanner, so the user must still review the
result before transferring it.

## Export existing Narnia jobs

From the **Scheduled Jobs** page:

1. Select one or more jobs.
2. Choose **Export for transfer** or **Export share template**.
3. Review the downloaded JSON before sending it elsewhere.

Agents can perform the same workflow with
[`export_schedule_package`](tools/export-schedule-package.md).

## Package a non-Narnia Windows task

Install Narnia on the source computer and use the `narnia-scheduler` skill:

1. Discover likely Copilot tasks with its read-only `Find-CopilotScheduledTasks.ps1` helper.
2. Select the exact task.
3. Inspect its triggers/action with `Read-ExistingScheduledTask.ps1`.
4. Read only the selected wrapper script and reconstruct an equivalent self-contained prompt.
5. Build a package with [`build_schedule_package`](tools/build-schedule-package.md).

Packaging does not register a local Narnia task or modify the original task.

## Import

1. Choose the package on the destination **Scheduled Jobs** page.
2. Preview it.
3. Map required repositories, directories, and files.
4. Resolve task-name conflicts, skip jobs that should not be imported in this batch, and review
   timezone/dependency warnings.
5. Refresh the preview until every intended job is importable.
6. Import.

Every imported task is registered **disabled from the beginning**. Narnia never briefly registers it
enabled and disables it afterward.

After import:

1. Save the downloaded receipt.
2. Perform a supervised dry run.
3. Configure missing secrets/profiles locally.
4. Explicitly enable the destination jobs.
5. Only then disable the original source tasks.

## Package trust

A package is executable intent: its prompt may instruct Copilot to edit files, call external
services, or use broad tool permissions. Import only packages from a trusted source, and review the
rendered prompt, allow flags, paths, and dependencies before enabling anything.

## Current limitations

- Windows Task Scheduler remains the only registration backend.
- Plugin and arbitrary prompt dependency discovery is best-effort.
- External durable state is reported but not copied.
- Packages are not cryptographically signed.
- There is no continuous synchronization between machines.
