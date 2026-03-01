---
description: Get workspace metadata for a Copilot CLI session including the git root directory and a list of session artifact files like plan.md and context files.
---

# get_session_workspace

Returns filesystem metadata for a session that supplements the database record: the git root directory (read from `workspace.yaml`) and the list of artifact files stored in the session's `files/` directory (plan.md, context files, etc.).

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sessionId` | string | Yes | The session GUID |

## Response

Returns a JSON workspace info object:

```json
{
  "sessionId": "0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4",
  "gitRoot": "C:\\dev\\nexus-labs\\needlr",
  "artifactFiles": [
    "C:\\Users\\Nick\\.copilot\\session-state\\0c531e17-...\\files\\plan.md",
    "C:\\Users\\Nick\\.copilot\\session-state\\0c531e17-...\\files\\context.md"
  ]
}
```

`artifactFiles` contains absolute paths to files the session stored as artifacts — typically `plan.md`, architecture diagrams, task breakdowns, or any file explicitly written during the session.

## Example Prompts

- "What workspace files does session abc123 have?"
- "Show the artifact files for my most recent needlr session"
- "Get the workspace info for session 0c531e17 — does it have a plan.md?"

## Notes

`gitRoot` is read from `~/.copilot/session-state/{id}/workspace.yaml`. It may differ from `cwd` if the session was started in a subdirectory. If the workspace.yaml file is absent or the `files/` directory does not exist, the fields will be `null` and an empty array respectively.
