---
description: Narnia records your open Windows Terminal windows of Copilot tabs so you can reopen a whole multi-tab window after it is closed or lost — like restoring a browser window.
---

# Terminal Window Recovery

A Windows update reboots your machine, or you press <kbd>Alt</kbd>+<kbd>F4</kbd> on
the wrong window, and an entire Windows Terminal full of Copilot tabs is gone.
Narnia continuously records your open terminal windows of Copilot tabs so you can
reopen the whole window in one click — like restoring a browser window.

Open the **🪟 Windows** page in the web UI to see your open and recently-closed
windows and reopen any of them.

## How it works

A background snapshotter inside the always-on Narnia server takes a snapshot of
your open terminal windows on a timer (once a minute by default). For each window
it records the Copilot tabs it contains and each tab's starting directory, writing
them to Narnia's own settings database. When a window disappears, it is marked
**closed** and retained so you can reopen it later.

Because the snapshot is taken *before* a window dies, recovery does not depend on
the window still being open — the last snapshot (at most one interval old) is
already saved.

!!! info "Detection is Windows Terminal–specific"
    Recording reconstructs a window by walking the process tree to the owning
    `WindowsTerminal.exe`. Resumed tabs are identified from
    `copilot --resume=<id>` in their process chain. Fresh sessions, whose command
    line has no session ID, are resolved through Copilot's live
    `inuse.<pid>.lock` marker. Tabs added to a window after it launched are still
    recorded, but their starting directory is only captured when it appears in
    the window's launch command.

### Copilot tabs only

The database only ever holds Windows Terminal windows that contain at least one
`copilot --resume` tab, and only those tabs. Other terminal windows and
non-Copilot tabs are never recorded.

## Reopening a window

On the **🪟 Windows** page, each recently-closed window shows its tabs. Click
**🚀 Reopen** to relaunch the whole window as a single Windows Terminal window,
one tab per session. Each tab resumes its Copilot session and opens in the
captured starting directory (falling back to the session's preferred path, working
directory, or git root when no directory was captured).

Reopening is always manual — Narnia never relaunches a window automatically.

### Pinning and retention

Closed windows are kept up to a configurable limit (50 by default); older ones are
pruned. **Name** a window to pin it so it is never pruned. Identical windows
(the same set of sessions) are deduplicated, so reopening and re-closing the same
working set keeps a single record rather than piling up duplicates.

## Settings

Configure recording on the **⚙️ Settings** page under *Terminal Window Snapshots*:

| Setting | Default | Notes |
|---------|---------|-------|
| Record terminal windows | On | Turn recording off/on at runtime without restarting the server. |
| Snapshot interval (seconds) | 60 | Minimum 5. |
| Closed windows to keep | 50 | Pinned windows are always kept. |

These map to settings keys (`snapshotter_enabled`, `snapshotter_interval_seconds`,
`snapshotter_retention_count`) read each tick, and to the
`NARNIA__SnapshotterEnabled`, `NARNIA__SnapshotterIntervalSeconds`, and
`NARNIA__SnapshotterRetentionCount` option defaults.

## Start at login (optional)

The snapshotter only runs while the Narnia server is up. The server is already
relaunched at the start of every Copilot CLI session by the
[`narnia-web-server`](skills/narnia-web-server.md) hook, so it is effectively
always up once you start using Copilot. To have it running from the moment you
sign in — even before your first session — enable **Start at Login** on the
Settings page (Windows only, off by default). This adds a per-user
`HKCU\…\CurrentVersion\Run` entry; no administrator rights are required.

## Headless snapshot (optional)

For belt-and-suspenders collection even when the web server is stopped, run a
single snapshot pass and exit:

```powershell
NexusLabs.Narnia.Web.exe snapshot
```

This applies any pending migrations, records the currently-open windows once, and
exits without starting the server — suitable for a Windows Scheduled Task that
runs at logon and repeats periodically.

## Where it is stored

All window data lives in Narnia's own settings database
(`<LocalAppData>/narnia/settings.db` — `%LOCALAPPDATA%\narnia\settings.db` on
Windows) in the `terminal_windows` and `terminal_window_tabs` tables. The Copilot
CLI's `session-store.db` is only ever read, never modified.

## Extending the sources

The recovery console reads windows through an `ITerminalWindowSource` contract
aggregated by `ITerminalWindowAggregator`. The live snapshotter is the built-in
source; additional sources can be registered and will appear in the console
automatically, exposing only the domain window model (a source's own storage is
its private concern).
