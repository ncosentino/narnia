---
description: List every Narnia-cataloged scheduled Copilot job joined to its live Windows Task Scheduler status, plus any untracked tasks in Narnia's scheduler folder.
---

# list_schedules

Returns every Narnia-owned scheduled Copilot job, joined to the live status of its Windows Task Scheduler task (state, last run result, next run time). Also surfaces any tasks found directly in Narnia's `\Narnia\` scheduler folder that aren't in the catalog (`untracked`), which normally indicates drift and is worth investigating.

## Parameters

None.

## Response

```json
{
  "schedulerSupported": true,
  "jobs": [
    {
      "job": {
        "id": "cca24e9e-1234-4a1b-9abc-000000000000",
        "name": "Example Daily Radar",
        "description": "Daily content-opportunity radar",
        "cwd": "C:\\dev\\example-repo",
        "prompt": "Run the example-radar skill with --lookback 24h.",
        "cadenceKind": "Daily",
        "cadenceTime": "05:00",
        "cadenceDays": null,
        "cadence": "Daily 05:00",
        "allowFlags": "--allow-all-tools --allow-all-paths",
        "copilotArgs": null,
        "taskFolder": "\\Narnia\\",
        "taskName": "Narnia - Example Daily Radar",
        "scriptPath": "C:\\Users\\me\\AppData\\Local\\narnia\\schedules\\cca24e9e.../run.ps1",
        "logDir": "C:\\Users\\me\\AppData\\Local\\narnia\\schedules\\cca24e9e.../logs",
        "createdAt": "2026-07-01T05:00:00-07:00",
        "updatedAt": "2026-07-01T05:00:00-07:00",
        "skills": [{ "skill": "example-radar", "resolution": "plugin" }]
      },
      "taskFound": true,
      "status": {
        "taskFolder": "\\Narnia\\",
        "taskName": "Narnia - Example Daily Radar",
        "state": "ready",
        "lastRunTime": "2026-07-01T05:00:03-07:00",
        "lastResult": 0,
        "nextRunTime": "2026-07-02T05:00:00-07:00",
        "actionSummary": "powershell.exe -File run.ps1"
      },
      "health": "interrupted",
      "lastRun": {
        "completion": "interrupted",
        "sessionId": "00000000-0000-4000-8000-000000000000",
        "abortReason": "user_initiated"
      }
    }
  ],
  "untracked": []
}
```

## Example Prompts

- "List all my scheduled Copilot jobs"
- "Show me every Narnia schedule and whether it's actually registered"
- "Are any of my scheduled jobs failing?"
- "Did any scheduled job get cut off before it finished?"

## Notes

`taskFound: false` means the catalog entry has no matching live task — the OS task was deleted or renamed outside Narnia. Use [`get_schedule`](get-schedule.md) for full detail on a single job, or [`create_schedule`](create-schedule.md)/[`update_schedule`](update-schedule.md) to re-register it.

### Do not read `lastResult: 0` as "the job did its work"

The Copilot CLI shuts down gracefully when it is interrupted, so a run that was killed part-way through still exits `0` and Windows Task Scheduler still records success. `health` and `lastRun` exist because of that: they come from the run's own Copilot session rather than from the exit code.

- `health: "interrupted"` — the scheduler reported success, but the session was aborted before it finished. Whatever the job was supposed to do at the end (write to a database, send a notification, open a pull request) probably never happened.
- `lastRun.completion` is `completed`, `interrupted`, or `unknown`. `unknown` asserts nothing: the log may be missing, name no session, or the session may have been cleaned up. It is never treated as a problem.
- `lastRun.abortReason` is the reason the session recorded. `user_initiated` is the CLI's interrupt path, which covers a `Ctrl+C` as well as the process being terminated by something else.
- `lastRun` is only present when the scheduler already reported success — every other health value comes from the scheduler itself and needs no second opinion.

See [Scheduled job health](../schedule-health.md) for the full classification.
