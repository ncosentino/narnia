---
description: How Narnia divides storage between the Copilot-owned session store it only reads and the settings database it owns, migrates, and is free to change.
---

# Data Storage Ownership

Narnia reads from databases and directories owned by the GitHub Copilot CLI and writes only to
storage it owns itself. Keeping that boundary explicit is what makes Narnia safe to run beside a
live Copilot installation.

| Store | Owner | Narnia's access | Default location |
|-------|-------|-----------------|------------------|
| `session-store.db` | GitHub Copilot CLI | Read, and write existing columns | `~/.copilot/session-store.db` |
| `session-state/` | GitHub Copilot CLI | Read | `~/.copilot/session-state/` |
| `installed-plugins/` | GitHub Copilot CLI | Read | `~/.copilot/installed-plugins/` |
| `settings.db` | Narnia | Full ownership, including schema | `<LocalAppData>/narnia/settings.db` |

All paths are configurable through `NarniaOptions`. See
[Configuration](../configuration.md) for the environment variables.

## The Copilot-owned session store

`session-store.db` is created and written by an external process — the Copilot CLI itself. Its
schema is defined and maintained by that tool, not by this repository.

**Narnia does not change that schema.** Adding, removing, or renaming a column, table, or index
would break compatibility with the CLI that owns the file and can corrupt session data that
users cannot regenerate. Reading from, and writing to, columns that already exist is fine.

The decision and its consequences are recorded in
[ADR-0001](../adr/adr-0001-copilot-owned-session-store-is-read-only.md).

### Overrides never hide recorded values

Narnia lets a user override values the CLI recorded — a session name, a repository, a branch.
Those overrides live in Narnia's own database and leave the session store untouched.

Because the underlying value still exists, the UI must never silently replace it. Any surface
that displays an overridden value also displays the original `session-store` value, so the user
can always see what the CLI actually recorded and tell the difference between Narnia's
presentation and Copilot's data.

## Narnia's own settings database

`settings.db` holds everything Narnia itself records: overrides, favorites, Collections, window
Layouts, settings, recorded terminal windows, storage metadata, scheduled-job catalog entries,
and session-migration history. Legacy Session Group rows remain stored for downgrade safety but
are no longer an active product model.

It lives in a dedicated per-app folder under the platform's local application data directory —
`%LOCALAPPDATA%\narnia\settings.db` on Windows, and the XDG or `~/Library/Application Support`
equivalent elsewhere, resolved through `Environment.SpecialFolder.LocalApplicationData`.

### Why not `~/.copilot`

`~/.copilot` belongs to the Copilot CLI. Its documentation reserves plugin-writable data for
`${COPILOT_PLUGIN_DATA}`, so a plugin that drops its own database beside the CLI's is writing
into someone else's directory.

A dedicated app-data folder matches what comparable tools do — the GitHub CLI, VS Code, and the
`platformdirs`/`env-paths` conventions all use a per-app directory — and it is already where
Narnia keeps `web-server.json`, its scheduled-job wrappers and logs, its recovery packets, and
its published `app/`. Keeping the settings database there makes the read-only boundary around
`~/.copilot` complete rather than almost complete.

This is recorded in
[ADR-0002](../adr/adr-0002-settings-database-lives-in-local-app-data.md).

### Relocation from the legacy path

Earlier versions stored this database at `~/.copilot/narnia-settings.db`. `SettingsDatabaseRelocator`
runs on startup, before migrations, and acts only when the current location is absent and a
legacy file is present.

The relocation is deliberately non-destructive. It **copies** the legacy database and its
`-wal`/`-shm` sidecars to the new location, then retires the legacy file by renaming it to a
timestamped `.bak`. Legacy bytes are never deleted, so an interrupted or failed relocation
always leaves a recoverable copy behind. The only files the relocator deletes are partial copies
it just wrote to the destination.

It is also best-effort and never fatal. If the legacy file is momentarily locked — an older
server still shutting down, for example — it is left in place and the relocation is retried on a
later launch.

## Schema migrations

Narnia migrates its own database with [DbUp](https://dbup.readthedocs.io/), via the
`dbup-sqlite` package pinned in `Directory.Packages.props`.

- Migration scripts live in `src/NexusLabs.Narnia.Core/Repositories/Migrations/` and are embedded
  into the Core assembly by an `EmbeddedResource` item in `NexusLabs.Narnia.Core.csproj`.
- Scripts are named `NNNN_description.sql`, for example `0001_initial.sql`. DbUp orders them by
  name, so the numeric prefix is what determines execution order.
- `NarniaSettingsDbMigrator` runs every pending script on startup. DbUp records which scripts it
  has already executed and runs only new ones.

To add a migration, create the next numbered file in that folder. Nothing else needs to be
registered.

Applied scripts are effectively immutable. Editing one that has already run on a user's machine
does not re-run it — DbUp has recorded it as executed — so the change silently never reaches
existing installations. Correct an applied migration by adding a new script.

### DbUp API notes

The `dbup-sqlite` 6.x API differs from what older samples and much of the search-result
literature show:

- The namespace is `DbUp.Sqlite`, not `DbUp.SQLite`.
- The builder method is `.SqliteDatabase(connectionString)` on `DeployChanges.To`.
- SQLite creates the database file on first connection but **not** its parent directory.
  `NarniaSettingsDbMigrator` creates the directory first; a migrator that does not will fail on a
  clean machine.
