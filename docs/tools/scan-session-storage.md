---
description: Queue a background metadata-only scan of local Copilot session-state disk usage through Narnia MCP.
---

# scan_session_storage

Queues a background session-storage scan. If a scan is already running or queued, the response
reports that the request was not accepted and includes current progress.

## Parameters

None.

## Response

Returns whether the request was accepted and the scanner's current status, progress, timestamps,
and latest error.

## Example Prompts

- "Refresh Narnia's session storage measurements"
- "Start a session storage scan"
