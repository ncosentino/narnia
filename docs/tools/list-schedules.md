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

## Notes

`taskFound: false` means the catalog entry has no matching live task — the OS task was deleted or renamed outside Narnia. Use [`get_schedule`](get-schedule.md) for full detail on a single job, or [`create_schedule`](create-schedule.md)/[`update_schedule`](update-schedule.md) to re-register it.
