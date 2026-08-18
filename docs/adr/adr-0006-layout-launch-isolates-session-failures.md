---
description: Why Layout launch skips invalid sessions and windows while continuing every safe part of the saved workspace.
---

# ADR-0006: Layout launch isolates session failures

**Status:** Accepted

## Context

Layouts can compose many Collections and individual sessions. The original launch preflight
treated any missing, active, duplicated, or colliding session as a reason to reject the complete
Layout before opening anything.

That all-or-nothing policy made a large saved workspace unusable when one stale Collection member
or one already-running session could suppress dozens of otherwise valid sessions across unrelated
windows.

## Decision

Only environment-wide failures block the complete Layout:

- Layout restore is unsupported on the current platform;
- Windows Terminal is unavailable;
- no monitor topology is available;
- no shell is configured; or
- the Layout has no windows.

Session and slot failures are isolated. Narnia filters and reports unavailable, active,
duplicated, directory-colliding, recovery-blocked, or otherwise unlaunchable sessions while
continuing every eligible session and window.

When the same session is represented by an individual-session window and by a Collection, the
individual-session window owns it. The Collection window still launches its remaining members.
Other duplicate assignments are resolved deterministically by slot order.

Directory collisions remain safe by default: the colliding requested session is skipped and
reported rather than forcing an unsafe launch or blocking unrelated windows.

The launch response includes every Layout window, its launched-session count, per-session
failures, placement result, and any detected collisions. A partial result is successful HTTP
execution but is not presented as a fully successful Layout.

## Alternatives considered

**Keep all-or-nothing preflight.** Simple and maximally conservative, but one stale member makes a
large Layout operationally useless.

**Skip an entire window when one member fails.** Better than rejecting the Layout, but still
throws away valid Collection members that can safely share the window.

**Force every collision automatically.** Maximizes launch count but violates the existing
working-directory safety boundary and risks concurrent edits in one worktree.

**Launch safe windows first, then ask whether to retry skipped collisions.** Potential future UX,
but retrying must avoid relaunching windows that already succeeded. The initial decision reports
the skipped sessions and leaves explicit forced retry to a separate workflow.

## Consequences

Large Layouts degrade gracefully as sessions age, move, or remain active. Users get the useful
portion of their workspace immediately and an in-app summary of everything skipped or partially
launched.

A window may open with fewer tabs than its Collection currently contains. Its result is marked
partial and lists each omitted session.

Top-level `success` now means every Layout window and session completed without failure; callers
must inspect window results when it is false rather than assuming nothing launched.
