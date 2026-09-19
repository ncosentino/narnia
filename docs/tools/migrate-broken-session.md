---
description: Recover a broken Copilot session in place while retaining its folder, session ID, Chronicle history, and Narnia references.
---

# `migrate_broken_session`

Archives the broken event stream, asks Copilot to reseed the same session ID and folder, and records
the recovery archive and integrity hash. Before seeding, Narnia includes bounded raw-event-tail evidence
when it is newer than the Chronicle index, so the successor does not rely on an older indexed turn as
its latest direction. Narnia never modifies Chronicle directly; the original stream remains archived
for inspection.

| Parameter | Description |
|-----------|-------------|
| `sessionId` | Source Copilot session GUID |
| `confirmMigration` | Must be `true`; acknowledges creation of a new session and one bootstrap model response |

The operation returns the same session ID. If a previously recovered session later becomes
incompatible again, a new recovery generation preserves the earlier migration record, recovery
packet, and archived event stream.
