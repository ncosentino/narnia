# Agent Guidelines for Narnia

## Database Schema — DO NOT MODIFY

The `session-store.db` SQLite database schema is **owned by the GitHub Copilot CLI** and is written to by an external process. **Do not add, remove, or rename columns, tables, or indexes in this database.** Reading from and writing to existing columns is fine.

### Why

The schema is defined and maintained by the Copilot CLI tool (not this repository). Altering it would break compatibility with the CLI and corrupt session data for users.

---

## Narnia Settings Database (`settings.db`)

Narnia owns a **separate** SQLite database at `<LocalAppData>/narnia/settings.db`
(`%LOCALAPPDATA%\narnia\settings.db` on Windows; the XDG/`~/Library/Application Support`
equivalent elsewhere, via `Environment.SpecialFolder.LocalApplicationData`). The path is
configurable via `NarniaOptions.SettingsDatabasePath`. This database is managed entirely by
this repository and deliberately lives in Narnia's **own** per-app folder — not inside the
Copilot-owned `~/.copilot` directory.

> **Why not `~/.copilot`?** `~/.copilot` is owned by the GitHub Copilot CLI; its docs reserve
> plugin-writable data for `${COPILOT_PLUGIN_DATA}`. Narnia only **reads** from `~/.copilot`
> (`session-store.db`, `session-state/`). Writing its own data to a dedicated app-data folder
> matches the universal convention (gh CLI, VS Code, platformdirs/env-paths) and is consistent
> with where Narnia already keeps `web-server.json` and its published `app/`.

### One-time migration from the legacy location

Earlier versions stored this database at `~/.copilot/narnia-settings.db`. On startup,
`SettingsDatabaseRelocator` runs before migrations and, if the new location is absent but the
legacy file exists, moves it (database plus any `-wal`/`-shm` sidecars) to the new path. It is
best-effort and never fatal: if the legacy file is momentarily locked (e.g. an old server is
still shutting down) it is left in place and the move is retried on a later launch.

### Schema migrations (DbUp)

- Migrations are embedded SQL scripts in `src/NexusLabs.Narnia.Core/Repositories/Migrations/`.
- File naming convention: `NNNN_description.sql` (e.g. `0001_initial.sql`).
- The `NarniaSettingsDbMigrator` service runs all pending migrations on app startup via `dbup-sqlite` v6.0.4.
- **To add a new migration:** create the next numbered `.sql` file in that folder; DbUp records which scripts have run and will only execute new ones.

### DbUp API notes (v6.0.4 quirks)

- Correct namespace: `DbUp.Sqlite` (not `DbUp.SQLite`)
- Correct method: `.SqliteDatabase(connectionString)` on `DeployChanges.To`
- SQLite auto-creates the database file on first connection, but **not** its parent directory; `NarniaSettingsDbMigrator` ensures the directory exists first.

### Overrides pattern

`session-store.db` values should **never be silently hidden**. Any UI that shows overridden values must also display the original `session-store` value so the user can see what the CLI recorded.

## Pull Request Delivery

- Deliver changes through feature branches and pull requests; never push directly to `main`.
  Local checkpoint commits are unrestricted before the branch is ready.
- Draft pull requests publish `Draft CI`. The default draft subset runs the Windows
  build and test job but skips release-package and documentation validation. Marking
  the pull request ready starts fresh full validation and publishes the stable `CI`
  check.
- Use a conventional pull request title of at most 72 characters. GitHub uses that
  title as the squash commit subject and the pull request body as the squash message.
- `GENESIS_REVIEW_POLICY=copilot-one-approval` requires a ready pull request authored
  by Copilot to receive one OWNER, MEMBER, or COLLABORATOR approval on the current
  head SHA. Pushing another commit requires approval of the new SHA.
- Approval for an external fork workflow authorizes the proposed workflow as a whole,
  including its runner selection. Review workflow changes and runner routing before
  approving execution. Narnia's pull request validation uses GitHub-hosted runners.
- Before marking a pull request ready, surface omitted behavior, implementation gaps,
  test results, technical debt, missing coverage, weak assertions, and assumptions.
