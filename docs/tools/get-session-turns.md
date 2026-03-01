---
description: Paginated conversation turn history for a Copilot CLI session. Returns user and assistant message pairs with timestamps.
---

# get_session_turns

Returns paginated conversation turns (user/assistant message pairs) for a session. Use this to read the actual conversation history when checkpoints alone don't provide enough context.

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `sessionId` | string | Yes | — | The session GUID |
| `offset` | integer | No | `0` | Number of turns to skip (for pagination) |
| `limit` | integer | No | `10` | Maximum number of turns to return |

## Response

Returns a JSON array of turn objects:

```json
[
  {
    "sessionId": "0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4",
    "turnIndex": 0,
    "userMessage": "Let's implement the authentication module...",
    "assistantResponse": "I'll start by creating the JwtService...",
    "timestamp": "2026-02-28T10:00:00Z"
  }
]
```

## Pagination

Use `offset` and `limit` together to page through long sessions:

```
offset=0, limit=10   → turns 0–9   (first page)
offset=10, limit=10  → turns 10–19 (second page)
```

For sessions with many turns, check `turnCount` from [`get_session_details`](get-session-details.md) to know when you've reached the end.

## Example Prompts

- "Show the first 10 turns of session abc123"
- "Get turns 50-60 from session 0c531e17..."
- "What was the conversation in the last few turns of this session?"

## Notes

Turns are returned in chronological order (ascending `turnIndex`). For most resume workflows, the checkpoints provide sufficient context — turn-level access is useful when you need to read exact tool calls or responses from a specific point in the conversation.
