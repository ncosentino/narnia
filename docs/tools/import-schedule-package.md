---
description: Import an accepted package preview as newly generated, disabled Narnia scheduled jobs.
---

# import_schedule_package

Imports a package only when the package content, bindings, job options, and destination state still
match a current preview fingerprint.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `packageJson` | string | Yes | Exact package content used for preview |
| `previewFingerprint` | string | Yes | Latest successful preview fingerprint |
| `bindings` | object[] | Yes | Same destination mappings used for preview, or `[]` |
| `jobs` | object[] | Yes | Same task-name/duplicate/skip choices used for preview, or `[]` |

Every imported job receives:

- A new local Narnia ID
- Locally generated wrapper and log paths
- The destination machine's configured Copilot command and PowerShell host
- A Task Scheduler entry registered disabled

The response includes an import receipt mapping portable job IDs to local Narnia IDs.
