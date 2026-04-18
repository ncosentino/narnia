---
name: narnia-web-server
description: >
  Start, stop, restart, and check status of the Narnia web UI server.
  Handles locating the Narnia source (local checkout or clone from GitHub),
  building, and running the Blazor web application on http://localhost:5244.
license: MIT
compatibility: Requires .NET 10+ SDK and git. Works on Windows, macOS, and Linux.
metadata:
  author: nexus-labs
  version: "1.0"
allowed-tools: Bash(*) PowerShell(*) Read Write Fetch
---

# Narnia Web Server

Manage the lifecycle of the Narnia web UI — a Blazor Server application that lets
you browse, search, and visualize your GitHub Copilot CLI session history.

## When to Use

- User asks to "open Narnia", "start the Narnia web UI", or "show my session history"
- User asks to stop, restart, or check the status of the Narnia web server
- The `open_narnia_ui` MCP tool failed or is unavailable

## Quick Reference

| Action  | What happens |
|---------|-------------|
| Start   | Locate source → `dotnet build` → `dotnet run --no-build` (detached) → health-check |
| Stop    | Find process listening on port 5244 → terminate it |
| Restart | Stop → Start |
| Status  | HTTP GET `http://localhost:5244` — report up/down |
| Update  | `git pull` in source dir → rebuild → restart if was running |

## Step-by-Step Procedures

### Locate the Narnia Source

The source is needed for **Start**, **Update**, and **Restart** (if rebuild is needed).

1. **Check for a local checkout.** Look for the Web project file at these paths (in order):
   - The plugin's own directory (walk up from this SKILL.md to find the repo root): `<repo-root>/src/NexusLabs.Narnia.Web/NexusLabs.Narnia.Web.csproj`
   - Common dev locations: `C:\dev\nexus-labs\narnia`, `~/dev/nexus-labs/narnia`, `~/source/narnia`
2. **If no local checkout exists**, clone from GitHub into a cache folder:
   ```bash
   # Choose a cache location
   CACHE_DIR="$HOME/.copilot/cache/narnia"    # Unix
   CACHE_DIR="$env:USERPROFILE\.copilot\cache\narnia"  # Windows

   # Clone if not already cached
   git clone https://github.com/ncosentino/narnia.git "$CACHE_DIR"
   ```
3. **Record the resolved path** as `$NARNIA_ROOT` for subsequent steps.

### Start

1. **Check if already running** — do a Status check first. If already up, tell the user and open the browser.
2. **Locate source** (see above).
3. **Build** (separate step so errors are visible):
   ```bash
   dotnet build "$NARNIA_ROOT/src/NexusLabs.Narnia.Web" --configuration Release
   ```
   If this fails, show the full error output to the user and stop.
4. **Run in background** using a detached process so it survives session shutdown:
   ```bash
   # The project's launchSettings.json binds to http://localhost:5244
   dotnet run --project "$NARNIA_ROOT/src/NexusLabs.Narnia.Web" --no-build --configuration Release
   ```
   Launch this as a **detached async shell** (mode="async", detach=true).
5. **Health-check** — poll `http://localhost:5244` every 2 seconds for up to 30 seconds:
   ```bash
   # PowerShell
   Invoke-WebRequest -Uri http://localhost:5244 -UseBasicParsing -TimeoutSec 2

   # Bash
   curl -sf --max-time 2 http://localhost:5244
   ```
6. **Report result** — if healthy, tell the user the URL. If not, read the process output for errors.
7. **Open browser** (optional, if user asked to "open" it):
   ```bash
   # PowerShell
   Start-Process "http://localhost:5244"

   # Bash / macOS
   open "http://localhost:5244"
   ```

### Stop

1. **Find the process** listening on port 5244:
   ```powershell
   # Windows PowerShell
   $conn = Get-NetTCPConnection -LocalPort 5244 -State Listen -ErrorAction SilentlyContinue
   $conn.OwningProcess
   ```
   ```bash
   # Unix
   lsof -ti :5244
   ```
2. **Terminate** the process by PID:
   ```powershell
   Stop-Process -Id <PID>
   ```
   ```bash
   kill <PID>
   ```
3. **Confirm** it stopped by re-checking port 5244.

### Restart

1. Run **Stop**.
2. Run **Start**.

### Status

1. HTTP GET `http://localhost:5244`:
   - **Success (2xx):** Report "Narnia web UI is running at http://localhost:5244"
   - **Connection refused / timeout:** Report "Narnia web UI is not running"
2. Optionally check if a process is listening on port 5244 for more detail.

### Update

1. **Locate source** (see above). Must be a git checkout (not just a published binary).
2. **Pull latest:**
   ```bash
   git -C "$NARNIA_ROOT" pull
   ```
3. **Check if currently running** (Status check).
4. If running: **Restart** (which rebuilds).
5. If not running: just **Build** to pre-warm the cache.

## Important Notes

- **Port:** The web server listens on `http://localhost:5244` (configured in `src/NexusLabs.Narnia.Web/Properties/launchSettings.json`).
- **Database:** The web UI reads from `~/.copilot/session-store.db` by default. No special config needed.
- **Build time:** First build can take 30-60+ seconds (NuGet restore + compile). Subsequent builds are fast (~5s).
- **Detached process:** Always use detached mode so the server keeps running after the LLM session ends.
- **.NET SDK required:** The user must have .NET 10+ SDK installed. If `dotnet` is not found, tell the user to install it from https://dot.net/download.
