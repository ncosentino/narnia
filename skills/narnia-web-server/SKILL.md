---
name: narnia-web-server
description: >
  Start, stop, restart, update, and check the status of the Narnia web UI — a Blazor Server app
  for browsing your GitHub Copilot CLI session history on http://127.0.0.1:5244. Runs from a
  published copy with a cross-session run-state file and a graceful HTTP shutdown, so rebuilds and
  updates never hit a file lock and any session can control a server another session started.
license: MIT
compatibility: Requires .NET 10+ SDK. Works on Windows, macOS, and Linux.
metadata:
  author: nexus-labs
  version: "1.0"
allowed-tools: Bash(*) PowerShell(*) Read Write Fetch
---

# Narnia Web Server

Manage the lifecycle of the Narnia server (a Blazor Server app) on `http://127.0.0.1:5244`. The
**same process also serves the MCP endpoint at `/mcp`** (Streamable HTTP), so starting this one
server makes narnia's session-history MCP tools available to **every** Copilot CLI session — they
all share this single instance (the plugin's `.mcp.json` points there as `type: http`).

## When to Use

- User asks to "open Narnia", "start the Narnia web UI", or "show my session history".
- User asks to stop, restart, update, or check the status of the Narnia web server.
- The `open_narnia_ui` MCP tool failed or is unavailable.

## Auto-start hook

The plugin ships a `sessionStart` hook (`hooks.json`) that **keeps the one server alive**: at the
start of every Copilot CLI session it checks `http://127.0.0.1:5244/health` and, if the server is
down **and already published** to the run dir, relaunches it (a fast no-op when it is already up).
This is what makes the shared HTTP MCP endpoint reliably reachable across sessions and machine
restarts.

The hook deliberately does **not** build/publish — that is this skill's job. So the **first**
start after a fresh install (run dir empty) is still done here (publish → launch); after that, the
hook relaunches the published server automatically. If a session reports narnia's MCP tools or UI
unavailable and the run dir has never been populated, run **Start** once.

## Design invariants (read before acting)

- **Run from a published copy, never in place.** The server is `dotnet publish`ed to a run
  directory and launched from there — *not* `dotnet run` from the source tree. Because the
  running process only locks the run directory, the source tree can be rebuilt or updated while
  the server is running, and an update simply re-publishes after a clean stop.
- **The running server owns a run-state file.** On startup it writes
  `<LocalAppData>/narnia/web-server.json` (`Pid`, `Port`, `Url`, `Version`, `ExePath`,
  `StartedAt`) and deletes it on graceful shutdown. Read it to discover and control a server that
  **any** session started.
- **Stop gracefully; never kill blindly.** Prefer `POST /shutdown` (loopback-only). A hard
  `Stop-Process` of the recorded `Pid` is a last resort, and only after verifying the live
  process's executable path equals the recorded `ExePath` (guards against PID reuse).

## Paths & port

| Thing | Location |
|-------|----------|
| Source | the narnia **plugin bundle** (resolve as below) |
| Run dir (published server) | `<LocalAppData>/narnia/app` |
| Run-state file (written by the server) | `<LocalAppData>/narnia/web-server.json` |
| Web UI | `http://127.0.0.1:5244` (loopback only) |
| MCP endpoint | `http://127.0.0.1:5244/mcp` (Streamable HTTP; shared by all sessions) |

`<LocalAppData>` is `%LOCALAPPDATA%` on Windows and the platform per-user data directory
elsewhere (`~/.local/share` or `$XDG_DATA_HOME` on Linux, `~/Library/Application Support` on macOS).

## Resolve the narnia source (deterministic — no clone, no guessing)

The server is built from the narnia **plugin bundle** — the repository this skill ships inside of.

1. **Explicit override (optional).** If the user supplies a narnia root, or `$env:NARNIA_REPO_PATH`
   is set, use it. It must contain `src/NexusLabs.Narnia.Web/NexusLabs.Narnia.Web.csproj`.
2. **Otherwise, the bundle.** The bundle root is two directories above this `SKILL.md`
   (`skills/narnia-web-server/` → bundle root). Confirm
   `<bundle>/src/NexusLabs.Narnia.Web/NexusLabs.Narnia.Web.csproj` exists.

There is **no** `git clone` and **no** well-known-path search. If neither resolves, abort with a
clear error. Getting newer narnia code is done by updating the plugin (`/plugin update narnia`),
not by cloning here. Record the resolved path as `$NARNIA_ROOT`.

## Quick reference

| Action  | What happens |
|---------|-------------|
| Status  | `GET /health` → up/down; read the run-state file for pid/port/version |
| Start   | If `/health` is up → reuse. Else publish → launch detached → poll `/health` |
| Stop    | `POST /shutdown` → wait for `/health` to go dark → fallback: identity-checked `Stop-Process` of the recorded pid |
| Restart | Stop → Start |
| Update  | Stop → re-publish from the bundle → Start |

## Procedures

### Status

`GET http://127.0.0.1:5244/health`:

- **200** → running. Optionally read `<LocalAppData>/narnia/web-server.json` for pid/port/version.
- **connection refused / timeout** → not running.

### Start (idempotent — never start a second instance)

1. **Status check first.** If `/health` returns 200, it is already running — report the URL (and
   open the browser if asked) and stop. Do not launch another instance.
2. **Resolve source** → `$NARNIA_ROOT` (see above).
3. **Publish to the run dir** (a frozen copy, decoupled from the source tree):
   ```powershell
   $runDir = Join-Path $env:LOCALAPPDATA 'narnia\app'
   dotnet publish "$NARNIA_ROOT/src/NexusLabs.Narnia.Web/NexusLabs.Narnia.Web.csproj" `
     -c Release -o $runDir
   ```
   If publish fails, show the full output to the user and stop.
4. **Launch detached** from the run dir, bound to loopback so it survives the session:
   ```powershell
   # Windows
   Start-Process -FilePath (Join-Path $runDir 'NexusLabs.Narnia.Web.exe') `
     -ArgumentList '--urls','http://127.0.0.1:5244' -WindowStyle Hidden
   ```
   ```bash
   # macOS / Linux — launch detached
   nohup dotnet "$runDir/NexusLabs.Narnia.Web.dll" --urls http://127.0.0.1:5244 >/dev/null 2>&1 &
   ```
   Use a detached background process (the CLI's detached/async mode) so the server keeps running
   after the session ends.
5. **Health-check.** Poll `http://127.0.0.1:5244/health` every second for up to ~40 seconds until
   it returns 200.
6. **Report** the URL; open the browser if the user asked:
   ```powershell
   Start-Process "http://127.0.0.1:5244"   # Windows; macOS: open, Linux: xdg-open
   ```

### Stop (graceful, works across sessions)

1. **Find the instance.** Prefer the run-state file `<LocalAppData>/narnia/web-server.json`
   (fields `Pid`, `Url`, `ExePath`). If it is absent, fall back to the process listening on port
   5244:
   ```powershell
   (Get-NetTCPConnection -LocalPort 5244 -State Listen -ErrorAction SilentlyContinue).OwningProcess
   ```
   ```bash
   lsof -ti :5244
   ```
2. **Graceful shutdown.** `POST <Url>/shutdown` (default `http://127.0.0.1:5244/shutdown`), then
   poll `/health` until it stops responding (up to ~15 seconds). The server removes its run-state
   file on exit.
3. **Fallback only if it did not exit.** Verify the recorded `Pid` is still alive **and** its
   executable path equals the recorded `ExePath` (never terminate an unrelated process that may
   have reused the pid), then `Stop-Process -Id <Pid>` (Unix: `kill <Pid>`). If the run-state
   file is stale (recorded pid is dead), just delete the file.
4. **Confirm** port 5244 is free.

### Restart

Run **Stop**, then **Start**.

### Update

Newer narnia code arrives by updating the plugin (`/plugin update narnia`), which replaces the
bundle this skill resolves. To roll a running server onto the current bundle:

1. **Stop** (graceful) — releases any file lock on the run dir.
2. **Re-publish** from `$NARNIA_ROOT` to the run dir (overwrites the previous copy; safe because
   the server is stopped).
3. **Start.**

Never re-publish or rebuild into the run dir while the server is running — stop it first.

## Important notes

- **Loopback only.** The server binds `127.0.0.1`; `/shutdown` rejects non-loopback callers.
- **Idempotent start.** A `/health` 200 means it is already up — reuse it.
- **Never kill blindly.** Only stop the pid recorded in the run-state file, only after the
  exe-path identity check, and prefer `POST /shutdown` over `Stop-Process`.
- **.NET SDK required.** .NET 10+. If `dotnet` is not found, tell the user to install it from
  https://dot.net/download.
- **Databases.** The UI reads `~/.copilot/session-store.db` (owned by the Copilot CLI — never
  modify its schema) and migrates its own `~/.copilot/narnia-settings.db` (overrides, settings,
  and the recorded terminal windows used by the **🪟 Windows** recovery console). While the
  server is running, a background snapshotter records open Windows Terminal windows of Copilot
  tabs once a minute so a closed window can be reopened.
