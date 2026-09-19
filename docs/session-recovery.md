---
description: Recover a Copilot session in place by archiving its broken event stream and asking Copilot to reseed the same folder and session ID.
---

# Broken Session Recovery

Copilot requires a persisted session event stream to begin with a valid `session.start` event.
Nested-agent histories and damaged event streams can instead fail with:

```text
Failed to resume session: Error: Session file is corrupted or incompatible
```

Launching such a session can leave you in an unrelated blank session. Narnia inspects the minimum resume contract before every terminal launch and blocks sessions that
are known to be incompatible.

Some older Copilot runtimes loaded the entire `events.jsonl` file as one in-memory string. They
rejected decoded streams beyond V8's 536,870,888-character ceiling and could silently create a blank
session. Narnia now inspects the installed Copilot package changelog read-only. When the selected
runtime explicitly advertises reliable very-large-session resume, Narnia allows native resume.
Missing, older, or malformed capability evidence keeps the conservative character-ceiling guard.
Migration previews report both the selected policy and detected Copilot package version.

## Recovering in Place

Open the source session's detail page. When Narnia detects incompatible history, it explains the
evidence and shows **Recover this session in place**.

Recovery is intentionally limited to histories Narnia can prove are incompatible: malformed start
records, missing required `session.start` events, empty streams, or streams too large for the
conservative compatibility guard. A session that still satisfies those constraints should be resumed
normally.

Recovery:

1. Reads indexed turns, checkpoints, workspace tasks, artifact names, and Narnia metadata.
2. Scans the raw event stream with bounded memory, retaining recent direct user direction separately
   from agent-generated steering. It compares the latest direct message with selected Chronicle turns
   so an incomplete index cannot make older instructions look current.
3. Writes a bounded recovery packet beneath `<LocalAppData>/narnia/recoveries/`. Direct raw user
   direction appears before indexed conversation and steering evidence.
4. Atomically renames only `events.jsonl` inside the affected session folder and records its
   SHA-256 hash.
5. Asks `GitHub.Copilot.SDK` to create the same session ID in the same folder.
6. Uses one tool-disabled bootstrap response to synthesize a working-state handoff.
7. Verifies the new event stream starts with `session.start`, the session is resumable, and
   Chronicle indexed the new turn.

If reseeding fails, Narnia archives the failed replacement stream and restores the original
`events.jsonl` atomically. Narnia never writes `session-store.db`; Chronicle changes are produced
only by Copilot.

## What Remains Attached

- The exact session ID and session-state folder
- Workspace tasks and `session.db`
- Checkpoints, artifacts, research, rewind snapshots, and generated workspace data
- Favorite state, alias, notes, repository/branch overrides, preferred path, and terminal title
- Collection and saved-window references
- Existing Chronicle turns and checkpoints; Copilot appends the recovery bootstrap as the next turn

Sessions with recorded recovery state are protected from normal Storage cleanup review. The
recovery packet can be downloaded from the session detail page or read in chunks through
`get_session_recovery_packet`.

If a recovered session later becomes incompatible again, Narnia can recover it again. Every
completed recovery retains its own migration record, recovery packet, and archived event stream.

## Limits

The archived event stream remains in the original folder for rollback and audit, but Copilot reads
only the new `events.jsonl`. Hidden model state cannot be reconstructed; the new active event stream
contains a bounded, high-signal handoff while the archived stream and recovery packet preserve the
older evidence.
