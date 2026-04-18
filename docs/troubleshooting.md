---
description: Troubleshooting common Narnia issues including web server startup failures, missing .NET SDK, incorrect database paths, and session search returning no results.
---

# Troubleshooting

## Web Server Does Not Start

**Symptom:** The web server fails to start or you can't reach `http://localhost:5244`.

**Fix:**

1. Make sure you have the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installed.
2. Try starting manually: `dotnet run --project src/NexusLabs.Narnia.Web` and check the output for errors.
3. Check if something else is already using port 5244 (`netstat -an | findstr 5244` on Windows).
4. If using the `narnia-web-server` skill, the LLM will show you the build output and can diagnose issues automatically.

---

## Web Server Starts But Can't Connect

**Symptom:** The server process is running but `http://localhost:5244` gives "connection refused."

**Fix:**

1. Wait a few seconds — cold builds can take 30-60+ seconds.
2. Check the server process output for binding errors.
3. If port 5244 is in use, change `applicationUrl` in `src/NexusLabs.Narnia.Web/Properties/launchSettings.json`.

---

## Sessions Not Found by Repository or CWD

**Symptom:** `list_sessions_by_repository` or `list_sessions_by_cwd` returns an empty array.

**Why it happens:** Path and repository matching is exact. The stored `cwd` must match exactly — including casing and whether it uses forward or backward slashes.

**Fix:**

- Use `list_recent_sessions` first to see actual `cwd` and `repository` values stored in the database.
- Copy the exact value from the results into your next query.
- On Windows, paths are stored with backslashes — use `C:\dev\myproject`, not `C:/dev/myproject`.

---

## .NET 10 SDK Not Found

**Symptom:** Running `dotnet run --project src/NexusLabs.Narnia.McpServer` fails with "The required .NET version was not found."

**Fix:** Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The Narnia projects target `net10.0` and require the .NET 10 runtime.

---

## Database File Not Found

**Symptom:** All MCP tools return errors mentioning "unable to open database file" or similar SQLite errors.

**Why it happens:** The default database path (`~/.copilot/session-store.db`) does not exist, or Narnia is looking in the wrong location.

**Fix:**

1. Confirm the file exists: on Windows, check `C:\Users\{your-username}\.copilot\session-store.db`.
2. If it's in a different location, set `NARNIA__DatabasePath` to the correct absolute path in your MCP config.
3. If you've never used Copilot CLI, the database won't exist yet — you need to start at least one Copilot CLI session first.

---

## Search Returns No Results

**Symptom:** `search_sessions` returns an empty array for queries that should match.

**Why it happens:** The FTS5 index may not contain the expected content, or the query syntax is incorrect.

**Fix:**

- Try a simpler query first: a single common word that should appear in your sessions.
- Use prefix matching for partial words: `auth*` instead of `authorization`.
- Verify sessions exist with `list_recent_sessions` — if that also returns nothing, the database path is likely wrong.
