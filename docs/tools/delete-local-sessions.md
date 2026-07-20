---
description: Permanently delete validated local Copilot session data through GitHub.Copilot.SDK after explicit confirmation.
---

# delete_local_sessions

Permanently deletes local session data through the official Copilot SDK. The operation re-runs all
safety checks immediately before deletion.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sessionIds` | string array | Yes | Copilot session IDs to delete locally |
| `overrideProtections` | boolean | Yes | Whether explicit Narnia protections may be overridden |
| `archiveDeletedSessions` | boolean | Yes | Whether successful deletions should be archived and hidden from normal Narnia views |
| `confirmLocalDeletion` | boolean | Yes | Must be `true` to acknowledge irreversible local deletion |

## Response

Returns one result per selected session plus deleted, archived, and estimated-byte totals.
Failures and safety blocks do not prevent independent safe sessions from being processed.

!!! warning "Local data only"
    Synced GitHub copies and Narnia aliases, notes, Collections, and Session Group references remain.
    Archiving only changes their default visibility in Narnia.

## Example Prompts

- "Preview these sessions, then delete only the locally safe ones"
- "Delete these local sessions and keep their Narnia Collection references"
