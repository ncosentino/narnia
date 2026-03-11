# Agent Guidelines for Narnia

## Database Schema — DO NOT MODIFY

The `session-store.db` SQLite database schema is **owned by the GitHub Copilot CLI** and is written to by an external process. **Do not add, remove, or rename columns, tables, or indexes in this database.** Reading from and writing to existing columns is fine.

### Why

The schema is defined and maintained by the Copilot CLI tool (not this repository). Altering it would break compatibility with the CLI and corrupt session data for users.
