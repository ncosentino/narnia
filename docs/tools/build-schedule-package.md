---
description: Build a portable schedule package from canonical definitions reconstructed from non-Narnia tasks.
---

# build_schedule_package

Builds a package without creating, registering, or modifying any scheduled job. Use it after the
`narnia-scheduler` skill has inspected a selected non-Narnia task and reconstructed its behavior.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `jobs` | object[] | Yes | Job definitions using the same prompt/cadence/skill fields as `create_schedule` |
| `dependencies` | object[] | Yes | Configuration or omitted external-state requirements; pass `[]` when none are known |
| `profile` | string | Yes | `transfer` or `share` |

Multiple triggers on one source task should be supplied as separate job definitions.
