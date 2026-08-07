---
description: List Copilot's persisted per-workspace sidebar tab lists.
---

# `list_sidebar_tabs`

Returns every workspace that has a persisted Copilot sidebar tab list, ordered by tab count.

Copilot replays these sessions as sidebar tabs whenever the folder is reopened and renders a
preview for each one, so an overlong or damaged list is a common cause of broken sidebar rendering
that survives `/restart`.

Each workspace reports its working directory, the backing state file, the tabs in restore order,
and per-tab annotations: whether the session still exists in the session store, whether a Copilot
runtime currently owns it, and its event-stream size.

`hasLiveRuntime` indicates a repair would be overwritten. Copilot rewrites the tab list when it
exits, merging its in-memory state over whatever is on disk.

This tool takes no parameters and modifies nothing.

See [Copilot Sidebar Tabs](../sidebar-tabs.md) for the full workflow.
