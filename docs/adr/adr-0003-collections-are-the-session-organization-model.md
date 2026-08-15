---
description: Why Collections are Narnia's only user-owned session organization model and legacy Session Groups remain stored but inactive.
---

# ADR-0003: Collections are the session organization model

**Status:** Accepted

## Context

Narnia developed two overlapping user-owned concepts. Session Groups were named, ordered sets
intended to reopen together. Collections were overlapping logical groupings that could span
repositories. Both selected and named sets of exact sessions, but each exposed different actions
and lived on a separate page.

Presenting them as adjacent tabs did not resolve the overlap. It preserved two decisions for the
user to understand and made organization depend on whether the immediate intent was categorizing
or launching sessions.

At the same time, Windows and process diagnostics are not user-owned groupings. They are observed
runtime information and need an information architecture that can expand without adding another
top-level navigation item for every diagnostic view.

## Decision

Collections are Narnia's single user-owned session organization model.

Collections retain overlapping membership and gain the useful launch behaviors previously
associated with Session Groups: opening every member together, choosing one tabbed window or
separate windows, opening only selected members, and capturing selected live sessions from the
Runtime area's Windows view.

Session Groups are retired rather than converted:

- existing Collections remain authoritative;
- legacy Session Group rows are not imported, renamed, or deleted;
- legacy UI routes redirect to Collections;
- legacy APIs return HTTP `410 Gone`;
- legacy membership no longer affects cleanup protection or session-recovery carry-forward.

Observed terminal windows and processes live under the **Runtime** navigation area at
`/runtime/windows` and `/runtime/processes`. The Runtime area is a presentation boundary, not a
persisted grouping model.

## Alternatives considered

**Keep both models behind one tabbed navigation item.** This reduced top-level links but left the
same conceptual duplication and forced users to choose between two nearly identical containers.

**Automatically convert Session Groups into Collections.** Rejected because names and memberships
can conflict with existing Collections, conversion would make legacy data authoritative over the
user's current Collection choices, and a silent merge would be difficult to reverse.

**Delete the Session Group tables and rows.** Rejected because deletion is irreversible and would
prevent a safe downgrade to a version that still understands those records.

**Keep legacy Session Groups hidden but behaviorally active.** Rejected because invisible
memberships would continue protecting sessions from cleanup and changing during recovery without
any current UI explaining why.

## Consequences

Users have one organizational concept and can launch a saved working set without creating a
parallel object. Runtime diagnostics can grow as nested views without crowding top-level
navigation.

Legacy Session Group data consumes a small amount of retained settings storage and remains
readable by older releases, but current Narnia deliberately ignores it. Downgrading restores the
old view of those unchanged records.

Collections do not yet expose manual membership ordering. Their launch order follows current
membership order. Adding explicit ordering later would require an additive settings migration and
a dedicated ordering interaction, but it would extend Collections rather than recreate Session
Groups.
