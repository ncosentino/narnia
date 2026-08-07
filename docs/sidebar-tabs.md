---
description: Inspect and repair Copilot's per-workspace sidebar tab list when session rendering breaks.
---

# Copilot Sidebar Tabs

Copilot CLI remembers which sessions were open in a folder and replays them as sidebar tabs the
next time that folder is opened. Narnia can show that list and repair it when it causes broken
rendering.

## Where the list lives

Copilot stores one file per working directory under `~/.copilot/sidebar-sessions-state/`. The file
name is the lowercase hex **SHA-256 of the working directory's exact bytes**, and the contents are
a small JSON document:

```json
{
  "schemaVersion": 1,
  "cwd": "C:\\dev\\nexus-labs\\narnia",
  "sessionIds": [
    "b0b9cddb-ef23-4f0f-8559-b2093c135332",
    "f483e90b-d870-4dc8-8d05-84990dc2e428"
  ]
}
```

The hash is taken byte-for-byte: no case folding, no separator normalization, and no
trailing-separator trimming. `C:\dev\narnia` and `c:/dev/narnia/` therefore produce three
independent tab lists for what is really one folder.

## The rendering symptom

Copilot renders a preview for every remembered tab. When a workspace accumulates a long tab list —
especially one containing very large sessions — the sidebar can render incorrectly: wrapped or
truncated output, overlapping text, or a redraw that will not settle.

Two properties make this hard to escape from inside Copilot:

- The tab list is **persisted**, so `/restart` reloads exactly the same state.
- Copilot **rewrites the file when it exits**, merging its in-memory tab list back over whatever is
  on disk. Editing the file underneath a running session is silently undone.

Narnia repairs the list from outside Copilot, which is why the fix sticks.

## Repairing from the session page

Open any session and expand **Copilot sidebar tabs for this folder**. Narnia shows every tab
Copilot would restore for that working directory, in restore order, annotated with:

- which entry is the session you are viewing;
- which entries a Copilot runtime currently owns (`live`);
- which entries no longer resolve to a session in the session store;
- each session's event-stream size, since that is what the sidebar preview has to render.

Two repairs are offered:

| Action | Effect |
| --- | --- |
| **Remove this session from the sidebar** | Drops one entry and leaves the rest of the tab list intact. |
| **Clear all sidebar tabs for this folder** | Empties the tab list for that working directory. |

Narnia copies the current list to a timestamped `.narnia-bak` file beside it before writing, so
repeated repairs never destroy the first known-good copy. Neither action deletes a session:
every session stays in the session store, searchable in Narnia and resumable by ID.

## Live sessions block a repair

If a Copilot runtime still owns one of the tabs, Narnia refuses the repair and says so rather than
writing a change that Copilot would overwrite on exit. Close those sessions and retry, or confirm
the prompt to force the write anyway.

Forcing is only useful when you intend to close the remaining sessions immediately afterwards —
otherwise the next clean shutdown restores the entries.

## Recommended order

1. Close the Copilot windows using that folder.
2. Repair the tab list in Narnia.
3. Relaunch Copilot in the folder.

## Verifying manually

```powershell
$path = "$HOME\.copilot\sidebar-sessions-state"
Get-ChildItem $path -Filter '*.json' | ForEach-Object {
    $state = Get-Content $_.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{ Cwd = $state.cwd; Tabs = $state.sessionIds.Count }
} | Sort-Object Tabs -Descending
```

## Agent access

The same data and repairs are available over MCP through
[`list_sidebar_tabs`](tools/list-sidebar-tabs.md) and
[`repair_sidebar_tabs`](tools/repair-sidebar-tabs.md).
