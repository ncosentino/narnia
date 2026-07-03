---
description: Update an existing Narnia-owned scheduled job, regenerating its wrapper script and re-registering its Windows scheduled task in place.
---

# update_schedule

Replaces an existing job's definition, regenerates its wrapper script, and re-registers its Windows scheduled task in place (trigger and action both refreshed). Every field is replaced — there is no partial update, so call [`get_schedule`](get-schedule.md) first and pass through anything you don't want to change.

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | string | Yes | | The job's Narnia id |
| `name` | string | Yes | | Display name for the job |
| `prompt` | string | Yes | | The full prompt passed to `copilot -p` |
| `cwd` | string | No | `null` | Working directory the job runs in |
| `description` | string | No | `null` | Short description |
| `cadenceKind` | string | No | `"daily"` | `"daily"`, `"weekly"`, or `"monthly"` |
| `time` | string | No | `"05:00"` | Local fire time, 24-hour `HH:mm` |
| `days` | string[] | No | `null` | Day names for a weekly cadence |
| `dayOfMonth` | integer | No | `null` | Day of month (1-31) for a monthly cadence |
| `allowFlags` | string | No | `"--allow-all-tools --allow-all-paths"` | Copilot allow-flags |
| `copilotArgs` | string | No | `null` | Extra arguments appended to the copilot invocation |
| `skills` | object[] | No | `null` | Skills/plugins the prompt invokes, in order |

## Response

```json
{ "ok": true, "id": "cca24e9e-1234-4a1b-9abc-000000000000" }
```

## Example Prompts

- "Update job cca24e9e... to run at 6:30am instead of 5am"
- "Change my example radar job's prompt to also check the last 14 days"

## Notes

Returns `Error: no scheduled job with id '...'.` if the id doesn't exist, or a plain error string if re-registration fails (e.g. unsupported platform). Every job is first-class and always editable this way — there is no separate "adopted" job format that can't be updated.
