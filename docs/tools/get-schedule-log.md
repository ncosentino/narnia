---
description: Read the latest per-run log for a Narnia scheduled job and determine whether it is still running.
---

# get_schedule_log

Reads the most recent log produced by a scheduled job. Use it after
[`run_schedule_now`](run-schedule-now.md), or when [`list_schedules`](list-schedules.md) reports a
failure or a currently-running task.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | string | Yes | The job's Narnia id |

## Response

```json
{
  "found": true,
  "path": "C:\\Users\\me\\AppData\\Local\\narnia\\schedules\\cca24e9e...\\logs\\run-2026-07-04_125106.log",
  "content": "=== Example Daily Radar ===\nStart: ...",
  "truncated": false,
  "isRunning": true
}
```

`isRunning` comes from the operating system scheduler's live state. When it is `true`, the log is
necessarily incomplete; poll again rather than treating the current content as a finished run.
Very large logs may return only their most recent portion with `truncated: true`.

If the job exists but has not written a log yet, `found` is `false` and `path`/`content` are null.
If the job id does not exist, the tool returns an error message.

## Example Prompts

- "Show me the latest log for my weekly internal linker"
- "Is job cca24e9e... still running, and what has it logged so far?"
