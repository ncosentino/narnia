---
description: Delete a Narnia scheduled job -- removes its Windows scheduled task, generated wrapper script, and catalog entry. Cannot be undone.
---

# delete_schedule

Deletes a job: removes its Windows scheduled task, its generated wrapper script, and its catalog entry. This cannot be undone.

!!! warning "Prefer disabling"
    If the job might be needed again later, use [`set_schedule_enabled`](set-schedule-enabled.md) with `enabled: false` instead. This is also the recommended approach when migrating an existing (non-Narnia) scheduled task into Narnia — disable the original rather than deleting it, until the new job has proven itself with a real run.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | string | Yes | The job's Narnia id |

## Response

```json
{ "ok": true, "id": "cca24e9e-1234-4a1b-9abc-000000000000" }
```

## Example Prompts

- "Delete job cca24e9e..., I don't need it anymore"
- "Remove my old example radar schedule"

## Notes

Returns `Error: no scheduled job with id '...'.` if the id doesn't exist. Confirm the id with [`list_schedules`](list-schedules.md) or [`get_schedule`](get-schedule.md) first if you're not certain.
