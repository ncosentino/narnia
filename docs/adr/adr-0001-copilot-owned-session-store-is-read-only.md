---
description: Why Narnia treats the Copilot CLI's session-store database as an external contract whose schema it never modifies.
---

# ADR-0001: The Copilot-owned session store is read-only for schema

**Status:** Accepted

## Context

Narnia's entire purpose is browsing, searching, and resuming GitHub Copilot CLI session history.
That history lives in `session-store.db`, a SQLite database created and continuously written by
the Copilot CLI itself, alongside a `session-state/` directory of per-session files.

Narnia is not the owner of that data. The CLI defines the schema, migrates it on its own release
schedule, and may hold the file open while Narnia reads it. SQLite makes it trivially easy for
Narnia to issue DDL against the same file, which raises a real question the codebase has to
answer once: may Narnia extend a store it does not own?

Extending it is tempting. Narnia records overrides, favorites, groups, and storage metadata that
all key off sessions, and a column on an existing table would be the cheapest possible join.

## Decision

Narnia treats `session-store.db` as an external contract.

Narnia does not add, remove, or rename any column, table, or index in that database. Reading from
it, and writing to columns that already exist, is permitted.

Everything Narnia itself records goes into a separate database that Narnia owns
([ADR-0002](adr-0002-settings-database-lives-in-local-app-data.md)).

## Alternatives considered

**Add Narnia-owned columns or tables to the session store.** Cheapest joins and one file to back
up. Rejected: the CLI owns the schema and migrates it independently. A future CLI migration could
collide with Narnia's additions, and a user's irreplaceable session history is the thing at risk.
It also makes Narnia's presence destructive to uninstall.

**Copy the session store and work against the copy.** Isolates Narnia completely. Rejected:
sessions change constantly while Copilot runs, so a copy is stale the moment it is taken, and
duplicating session history doubles disk usage for the one dataset users are already trying to
manage.

**Wrap the store behind an abstraction that permits schema changes under review.** Rejected: a
rule enforced only by review is exactly the rule that erodes. The boundary is worth more as an
absolute.

## Consequences

Narnia can be installed and removed without altering a byte of the user's Copilot data, and stays
compatible across CLI upgrades that change the schema.

The cost is a second database and cross-database joins performed in application code rather than
in SQL. `OverridingSessionRepository` and `OverridingSessionSearch` exist to compose the two
stores; that composition layer is the price of this decision, not an accident.

Because Narnia's values are layered on top of recorded ones rather than replacing them, the UI
must show both — see the overrides rule in
[Data Storage Ownership](../design/data-storage.md#overrides-never-hide-recorded-values).

If the CLI ever removes a column Narnia reads, Narnia breaks at read time rather than corrupting
data. That is the intended failure direction.
