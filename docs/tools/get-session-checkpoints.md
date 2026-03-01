---
description: Retrieve all structured checkpoints for a Copilot CLI session. Each checkpoint includes overview, history, work done, technical details, important files, and next steps.
---

# get_session_checkpoints

Returns all checkpoints for a session. Checkpoints are the richest source of context for resuming a session — each one contains a structured summary of what was accomplished, what changed, and what comes next.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sessionId` | string | Yes | The session GUID |

## Response

Returns a JSON array of checkpoint objects ordered by `checkpointNumber`:

```json
[
  {
    "id": 42,
    "sessionId": "0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4",
    "checkpointNumber": 3,
    "title": "Implemented JWT authentication",
    "overview": "Added JWT-based auth to the API...",
    "history": "Previous attempts used session cookies...",
    "workDone": "Created AuthController, JwtService...",
    "technicalDetails": "Using System.IdentityModel.Tokens.Jwt 7.x...",
    "importantFiles": "src/Auth/JwtService.cs, src/Auth/AuthController.cs",
    "nextSteps": "Add refresh token endpoint...",
    "createdAt": "2026-02-28T16:00:00Z"
  }
]
```

## Checkpoint Sections

| Field | Content |
|-------|---------|
| `title` | Short title for the checkpoint |
| `overview` | High-level summary of the session state |
| `history` | What was tried before, design decisions |
| `workDone` | Specific files and changes made |
| `technicalDetails` | Architecture notes, package versions, constraints |
| `importantFiles` | Key files changed or referenced |
| `nextSteps` | Planned work when the checkpoint was saved |

## Example Prompts

- "Show all checkpoints for session 0c531e17..."
- "What were the next steps in session abc123? Show me its checkpoints."
- "Get the checkpoint history for this session so I can resume it"

## Notes

Read the last checkpoint (highest `checkpointNumber`) to quickly restore the most recent context before resuming a session.
