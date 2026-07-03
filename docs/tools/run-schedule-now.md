---
description: Start a Narnia scheduled job's Windows scheduled task immediately, out of band from its normal cadence.
---

# run_schedule_now

Starts a job's Windows scheduled task immediately, independent of its normal cadence. Useful for testing a job right after creating or updating it, without waiting for its next scheduled fire time.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | string | Yes | The job's Narnia id |

## Response

```json
{ "ok": true, "id": "cca24e9e-1234-4a1b-9abc-000000000000" }
```

## Example Prompts

- "Run my example radar job right now"
- "Kick off job cca24e9e... immediately so I can check the log"

## Notes

This starts the task asynchronously — the tool returns as soon as Task Scheduler accepts the start request, not when the job finishes. Check progress via the job's log directory (`logDir` from [`get_schedule`](get-schedule.md)) or its next `lastRunTime`/`lastResult` from [`list_schedules`](list-schedules.md).
