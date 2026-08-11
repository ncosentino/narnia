---
applyTo: "src/NexusLabs.Narnia.Core/Repositories/Migrations/*.sql"
description: Naming and immutability rules for Narnia settings database migration scripts.
---

# Settings database migrations

These scripts migrate Narnia's own `settings.db`. They are embedded into the Core assembly and
executed by `NarniaSettingsDbMigrator` on startup.

Background: [Schema migrations](../../docs/design/data-storage.md#schema-migrations).

## Naming

Use `NNNN_description.sql` with the next unused four-digit prefix, for example
`0015_add_widget_state.sql`.

DbUp orders scripts by name, so the prefix determines execution order. Reusing or skipping a
number produces an order that does not match the intended sequence.

## Never edit an applied script

A script that has already run on a user's machine is recorded as executed and will not run again.
Editing it changes nothing for existing installations while silently changing what a fresh
installation gets — the two diverge permanently.

Correct or extend an applied migration by adding a new script.

## Registration

Nothing to register. `NexusLabs.Narnia.Core.csproj` embeds
`Repositories\Migrations\*.sql` as a resource, and the migrator discovers every embedded script
whose name contains `Migrations`.

## Content

Write statements that succeed against a database at the immediately preceding migration state.
Never issue DDL against the Copilot-owned `session-store.db` from here; these scripts only ever
target Narnia's own database.
