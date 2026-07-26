---
description: Inspect a schedule package against the destination without changing Narnia or Task Scheduler.
---

# preview_schedule_package

Validates a package and returns:

- Required path/repository bindings
- Task-name conflicts
- Prior imports
- Missing plugin or repo-local skills
- Timezone warnings
- Rendered destination definitions
- A `previewFingerprint`

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `packageJson` | string | Yes | Complete package content |
| `bindings` | object[] | Yes | `{ "id": "...", "value": "..." }` destination mappings; pass `[]` initially |
| `jobs` | object[] | Yes | Task-name overrides, duplicate-import decisions, and per-job `skip`; pass `[]` for package defaults |

Preview is side-effect-free. Re-run it after changing any binding or task name.
