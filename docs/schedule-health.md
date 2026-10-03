---
description: How Narnia classifies scheduled job health, and why a successful exit code does not prove a scheduled Copilot run finished its work.
---

# Scheduled job health

Narnia is not a scheduler. Windows Task Scheduler owns the tasks, and Narnia reports what it
says. A successful process exit, though, does not prove the agent finished its work.

## The problem with the exit code

When the Copilot CLI is interrupted, it does not crash. It records an `abort` event, writes its
usage checkpoint, shuts down, and **exits `0`**.

The CLI can also record a `session.warning` with
`warningType: "background_task_wait_timeout"`, abandon pending background work, and exit `0`
with a routine shutdown. A routine shutdown is not evidence against that warning.

Nothing downstream can tell that apart from a healthy run:

- Task Scheduler records `Last Run Result: 0x0`.
- The wrapper script Narnia generates passes that exit code straight through.
- The Schedules page shows `ok`.

So a job that was killed a minute before it wrote to a database, sent an email, or opened a pull
request looks exactly like one that did all of it. The only surviving evidence is in the run's
own Copilot session.

## What Narnia checks

When — and only when — the scheduler already reported success, Narnia looks at how the run's
session actually ended:

1. It reads the tail of the job's newest run log and finds the `--resume=<session>` footer the CLI
   prints when it exits.
2. It reads the tail of that session's `events.jsonl`.
3. It checks for the CLI's explicit warning that it stopped waiting with background work pending.
   That warning in the run log is enough to report **interrupted**, even without a session footer
   or a surviving session directory.
4. Otherwise it classifies the session tail: an unresolved `abort` or
   `background_task_wait_timeout` warning means **interrupted**. Completion requires a shutdown;
   ordinary tool or assistant events alone do not prove completion.

An abort that is followed by more work (a new user message or a new assistant turn) does not
count — that is an interactive cancel the session recovered from, not the thing that ended it.

Background timeout warnings are different: in-flight assistant turns, tool completions, and child
agent completions do not erase the interruption. A new user prompt can begin a new attempt in the
session, but cannot retroactively fix the scheduled run whose own log records abandonment.
The log fallback also covers a warning pushed outside the event tail by a large usage checkpoint.
Unrelated warnings and ordinary tool timeouts are not classified as terminal interruptions.

Both reads are bounded tails. Run logs accumulate over months and a session's event stream can
reach hundreds of megabytes, and this runs once per job every time the schedule list is rendered.

## The classifications

| Health | Meaning |
| --- | --- |
| `ok` | The scheduler reported success and nothing contradicts it. |
| `interrupted` | The scheduler reported success, but the run was aborted or abandoned pending background work. |
| `failed (0x…)` | The scheduler reported a failure result. |
| `running` | The task is executing now. |
| `drift` | A cataloged job has no matching scheduled task. |
| `never run`, `disabled`, and the rest | Passed through from the scheduler's own status. |

`interrupted` and `failed` both require attention, and both link to the run log.

## When Narnia stays quiet

The check only ever downgrades success, and only on positive evidence. It says nothing when:

- The job has never run, or its log directory is missing.
- The log names no session and contains no recognized terminal warning.
- The session folder has been cleaned up and the log contains no recognized terminal warning.
- The event stream cannot be read, or the tail contains no complete lines.

All of those report `unknown`, which leaves the health as the scheduler reported it. A warning
that fires on healthy jobs hides the real ones, so an unreadable run is never treated as a
problem.

## Reading it from an agent

[`list_schedules`](tools/list-schedules.md) returns `health` and `lastRun` per job:

```json
{
  "taskFound": true,
  "status": { "state": "ready", "lastResult": 0 },
  "health": "interrupted",
  "lastRun": {
    "completion": "interrupted",
    "sessionId": "00000000-0000-4000-8000-000000000000",
    "abortReason": "user_initiated"
  }
}
```

`abortReason` is the recorded abort reason or terminal warning type.
`background_task_wait_timeout` means Copilot abandoned pending background work.
`user_initiated` is the CLI's interrupt path: it
covers a `Ctrl+C` and equally the process being terminated by something else, so it does not by
itself prove a person did it.

## What it will not tell you

Narnia can say a run was cut short, and an explicit background timeout identifies one cause.
An abort alone does not identify its origin. Start from the job's log to see how far the run got,
then look for an
external cause (a machine going to sleep, a session ending, a process being killed) around the
timestamp of the last log line.

Even `completed` means only that no recognized interruption contradicted a recorded shutdown.
It does not verify an article insert, an email delivery, or any other application-specific result.
Narnia does not automatically retry interrupted jobs: a partial run may already have performed
side effects. Check existing output and finish only the missing work. Raising
`COPILOT_TASK_WAIT_TIMEOUT_SECONDS` may delay a cutoff, but does not repair false success reporting
or establish that the job finished.
