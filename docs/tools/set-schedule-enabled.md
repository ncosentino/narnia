---
description: Enable or disable a Narnia scheduled job's Windows scheduled task without deleting it.
---

# set_schedule_enabled

Enables or disables a job's Windows scheduled task without touching its catalog entry, wrapper script, or logs. Prefer this over [`delete_schedule`](delete-schedule.md) whenever a job might be needed again later — e.g. pausing a job during a migration, or temporarily silencing a noisy one.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | string | Yes | The job's Narnia id |
| `enabled` | boolean | Yes | `true` to enable, `false` to disable |

## Response

```json
{ "ok": true, "id": "cca24e9e-1234-4a1b-9abc-000000000000" }
```

## Example Prompts

- "Disable my example radar job for now"
- "Re-enable job cca24e9e..."

## Notes

Returns `Error: no scheduled job with id '...'.` if the id doesn't exist. Check the result with [`list_schedules`](list-schedules.md) — a disabled job reports `status.state: "disabled"`.
