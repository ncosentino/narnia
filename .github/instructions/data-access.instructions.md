---
applyTo: "src/NexusLabs.Narnia.Core/Repositories/**"
description: Database ownership boundaries and DbUp usage for Narnia's data-access layer.
---

# Data access

This layer spans two SQLite databases with different owners. Getting the owner wrong corrupts
user data that cannot be regenerated.

Background and rationale: [Data Storage Ownership](../../docs/design/data-storage.md),
[ADR-0001](../../docs/adr/adr-0001-copilot-owned-session-store-is-read-only.md),
[ADR-0002](../../docs/adr/adr-0002-settings-database-lives-in-local-app-data.md).

## The Copilot session store is schema read-only

`session-store.db` and everything else under `~/.copilot` is owned by the GitHub Copilot CLI.

- Never issue DDL against it: no `CREATE`, `ALTER`, or `DROP` of any table, column, or index, and
  no new table for Narnia's own data.
- Reading, and writing to columns that already exist, is allowed.
- Narnia-owned state goes in the settings database instead. Compose the two stores in application
  code, as `OverridingSessionRepository` and `OverridingSessionSearch` already do.

This prevents a CLI upgrade from colliding with Narnia's additions and destroying irreplaceable
session history.

## The settings database is Narnia's

`settings.db` is owned by this repository. Change its schema only by adding a DbUp migration
script — never with inline DDL at runtime.

Its location resolves through `NarniaOptions.SettingsDatabasePath`. Do not hardcode a path, and
do not place Narnia-owned data inside `~/.copilot`.

Honor `NarniaOptions.SettingsConnectionString` when it is set: it overrides the file path and is
how tests run against in-memory SQLite. Code that skips it makes itself untestable.

## DbUp

`dbup-sqlite` is pinned in `Directory.Packages.props`. The 6.x API differs from most published
samples:

- The namespace is `DbUp.Sqlite`, not `DbUp.SQLite`.
- The builder method is `.SqliteDatabase(connectionString)` on `DeployChanges.To`.
- SQLite creates the database file on first connection but not its parent directory. Create the
  directory first or migration fails on a clean machine.
