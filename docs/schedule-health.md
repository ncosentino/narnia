---
description: How Narnia classifies scheduled job health, and why a successful exit code does not prove a scheduled Copilot run finished its work.
---

# Scheduled job health

Narnia is not a scheduler. Windows Task Scheduler owns the tasks, and Narnia reports what it
says. For one specific case, though, the scheduler is not a reliable narrator.

## The problem with the exit code

When the Copilot CLI is interrupted, it does not crash. It records an `abort` event, writes its
usage checkpoint, shuts down, and **exits `0`**.

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
3. It classifies the ending: a session whose last act was an `abort` was **interrupted**.

An abort that is followed by more work (a new user message or a new assistant turn) does not
count — that is an interactive cancel the session recovered from, not the thing that ended it.

Both reads are bounded tails. Run logs accumulate over months and a session's event stream can
reach hundreds of megabytes, and this runs once per job every time the schedule list is rendered.

## The classifications

| Health | Meaning |
| --- | --- |
| `ok` | The scheduler reported success and nothing contradicts it. |
| `interrupted` | The scheduler reported success, but the run's session was aborted before it finished. |
| `failed (0x…)` | The scheduler reported a failure result. |
| `running` | The task is executing now. |
| `drift` | A cataloged job has no matching scheduled task. |
| `never run`, `disabled`, and the rest | Passed through from the scheduler's own status. |

`interrupted` and `failed` both require attention, and both link to the run log.

## When Narnia stays quiet

The check only ever downgrades success, and only on positive evidence. It says nothing when:

- The job has never run, or its log directory is missing.
- The log names no session — for example a run that died before Copilot started.
- The session folder has been cleaned up.
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

`abortReason` is whatever the session recorded. `user_initiated` is the CLI's interrupt path: it
covers a `Ctrl+C` and equally the process being terminated by something else, so it does not by
itself prove a person did it.

## What it will not tell you

Narnia can say a run was cut short. It cannot say what cut it short — the session records the
abort, not its origin. Start from the job's log to see how far the run got, then look for an
external cause (a machine going to sleep, a session ending, a process being killed) around the
timestamp of the last log line.
