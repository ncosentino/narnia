---
description: Get cached Copilot session-storage totals, scan health, and the largest local sessions through Narnia MCP.
---

# get_session_storage_overview

Returns cached logical disk usage for local Copilot session state, the latest scan result, and the
25 largest measured sessions.

## Parameters

None.

## Response

The JSON response includes category totals, local/history-only/state-only counts, active and
protected counts, scan completeness, and the largest sessions with growth and risk flags.

## Example Prompts

- "Show my Copilot session storage usage"
- "Which local sessions are consuming the most disk space?"
- "Are any session storage scans incomplete?"
