---
description: Read a bounded chunk from the Narnia-owned history packet for a migrated source or successor session.
---

# `get_session_recovery_packet`

Returns recovery-packet text for exact historical lookup after migration.

| Parameter | Description |
|-----------|-------------|
| `sessionId` | Source or successor Copilot session GUID |
| `offset` | Zero-based character offset |
| `maxCharacters` | Maximum returned characters, clamped to 50,000 |

Use `nextOffset` to continue until it is `null`.
