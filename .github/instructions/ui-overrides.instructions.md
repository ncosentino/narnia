---
applyTo: "src/NexusLabs.Narnia.Web/Components/**"
description: Rules for rendering session values that Narnia overrides on top of Copilot-recorded data.
---

# UI components

Background: [Overrides never hide recorded values](../../docs/design/data-storage.md#overrides-never-hide-recorded-values).

## Never hide a recorded value behind an override

Narnia lets users override values the Copilot CLI recorded — session name, repository, branch,
working directory. The override lives in Narnia's database; the recorded value still exists and
is unchanged.

Any surface that displays an overridden value must also display the original `session-store`
value. Replacing it silently makes Narnia's presentation indistinguishable from Copilot's data
and leaves the user unable to tell what the CLI actually recorded.

Showing the original as secondary text, a tooltip, or a detail row all satisfy this; showing only
the override does not.

## Do not act on an override as if it were recorded fact

An override is a label. When a component drives a real operation from a value — launching a
session, resolving a worktree, opening a directory — use the value that operation will actually
receive, and surface a mismatch rather than implying the override changed anything.
