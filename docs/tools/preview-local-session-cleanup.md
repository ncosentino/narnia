---
description: Dry-run local Copilot session deletion and see allowed, protected, and blocked sessions before removing data.
---

# preview_local_session_cleanup

Validates selected sessions without deleting data. Always call this before
[`delete_local_sessions`](delete-local-sessions.md).

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sessionIds` | string array | Yes | Copilot session IDs to validate |
| `overrideProtections` | boolean | Yes | Whether explicit Narnia protections may be overridden |

## Response

Returns decisions with `allowed`, `protected`, or `blocked` disposition, estimated logical bytes,
and the reasons for each decision.

## Example Prompts

- "Preview deleting these old local sessions"
- "How much space would these sessions reclaim?"
- "Explain why this session is protected from cleanup"
