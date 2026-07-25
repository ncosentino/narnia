---
description: Recover a broken Copilot session in place while retaining its folder, session ID, Chronicle history, and Narnia references.
---

# `migrate_broken_session`

Archives the broken event stream, asks Copilot to reseed the same session ID and folder, and records
the recovery archive and integrity hash. Narnia never modifies Chronicle directly.

| Parameter | Description |
|-----------|-------------|
| `sessionId` | Source Copilot session GUID |
| `confirmMigration` | Must be `true`; acknowledges creation of a new session and one bootstrap model response |

The operation is idempotent after completed recovery and returns the same session ID.
