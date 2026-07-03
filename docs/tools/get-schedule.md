---
description: Get a single Narnia-cataloged scheduled Copilot job by id, including its full prompt and cadence.
---

# get_schedule

Returns the full catalog entry for one scheduled job, including its complete prompt — useful before calling [`update_schedule`](update-schedule.md), which replaces every field, so you can see current values first.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | string | Yes | The job's Narnia id, from [`list_schedules`](list-schedules.md) |

## Response

```json
{
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
}
```

This is the catalog entry only — it does not include live Task Scheduler status. Use [`list_schedules`](list-schedules.md) if you also need `taskFound`/`status`.

## Example Prompts

- "Show me the full prompt for my example radar job"
- "What cadence is job cca24e9e... running on?"

## Notes

If the id doesn't exist, the tool returns a plain error message (`Error: no scheduled job with id '...'.`) rather than throwing.
