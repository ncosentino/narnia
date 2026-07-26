---
description: Export selected Narnia scheduled jobs into a portable transfer package or share template.
---

# export_schedule_package

Exports one or more existing Narnia jobs as a versioned JSON package.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `jobIds` | string[] | Yes | Narnia job IDs from `list_schedules` |
| `profile` | string | Yes | `transfer` retains non-secret source hints; `share` removes them |

The response includes `packageJson`, portability warnings, and the parsed package. Write the exact
`packageJson` to a `.narnia-schedules.json` file; changing its definition content invalidates its
fingerprints.

Generated wrappers, task XML, logs, databases, and secrets are never exported.
