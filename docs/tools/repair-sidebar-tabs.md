---
description: Repair one workspace's Copilot sidebar tab list, backing up the current list first.
---

# `repair_sidebar_tabs`

Rewrites a single workspace's Copilot sidebar tab list. Narnia copies the current list to a
timestamped `.narnia-bak` file beside it before writing.

**No session is deleted.** Removing a tab only stops Copilot from restoring it in the sidebar; the
session remains in the session store, searchable in Narnia and resumable by ID.

| Parameter | Description |
|-----------|-------------|
| `cwd` | Working directory exactly as Copilot recorded it. The state file is keyed on these exact bytes, so casing and separators must match. |
| `sessionIds` | Session IDs to remove. Omit or leave empty to clear the entire tab list. |
| `force` | Applies the repair even when a live Copilot runtime would overwrite it on exit. |

By default the repair is **refused** when a Copilot runtime still owns one of the tabs, because
Copilot merges its in-memory tab list back over the file during shutdown and would undo the change.
Close those sessions and retry, or pass `force` when you intend to close them immediately
afterwards.

Use [`list_sidebar_tabs`](list-sidebar-tabs.md) first to confirm the exact `cwd` and inspect what
would be removed.

See [Copilot Sidebar Tabs](../sidebar-tabs.md) for the full workflow.
