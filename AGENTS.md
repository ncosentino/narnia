# Agent Guidelines for Narnia

## Database Schema — DO NOT MODIFY

The `session-store.db` SQLite database schema is **owned by the GitHub Copilot CLI** and is written to by an external process. **Do not add, remove, or rename columns, tables, or indexes in this database.** Reading from and writing to existing columns is fine.

### Why

The schema is defined and maintained by the Copilot CLI tool (not this repository). Altering it would break compatibility with the CLI and corrupt session data for users.

---

## Narnia Settings Database (`narnia-settings.db`)

Narnia owns a **separate** SQLite database at `~/.copilot/narnia-settings.db` (configurable via `NarniaOptions.SettingsDatabasePath`). This database is managed entirely by this repository.

### Schema migrations (DbUp)

- Migrations are embedded SQL scripts in `src/NexusLabs.Narnia.Core/Repositories/Migrations/`.
- File naming convention: `NNNN_description.sql` (e.g. `0001_initial.sql`).
- The `NarniaSettingsDbMigrator` service runs all pending migrations on app startup via `dbup-sqlite` v6.0.4.
- **To add a new migration:** create the next numbered `.sql` file in that folder; DbUp records which scripts have run and will only execute new ones.

### DbUp API notes (v6.0.4 quirks)

- Correct namespace: `DbUp.Sqlite` (not `DbUp.SQLite`)
- Correct method: `.SqliteDatabase(connectionString)` on `DeployChanges.To`
- No `EnsureDatabase.For` call needed — SQLite auto-creates the file on first connection

### Overrides pattern

`session-store.db` values should **never be silently hidden**. Any UI that shows overridden values must also display the original `session-store` value so the user can see what the CLI recorded.
