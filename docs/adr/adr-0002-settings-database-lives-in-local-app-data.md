---
description: Why Narnia stores its own settings database in a dedicated per-app local application data folder instead of inside the Copilot-owned home directory.
---

# ADR-0002: Narnia's settings database lives in local application data

**Status:** Accepted

## Context

[ADR-0001](adr-0001-copilot-owned-session-store-is-read-only.md) established that Narnia does not
change the schema of the Copilot CLI's session store. That decision requires Narnia to have a
database of its own for overrides, favorites, session groups, work collections, settings,
recorded terminal windows, storage metadata, scheduled-job catalog entries, and migration
history.

Earlier versions placed that database at `~/.copilot/narnia-settings.db` — beside the CLI's own
files. It was convenient: one directory held everything session-related, and Narnia was already
reading from that directory.

It also meant Narnia wrote into a directory owned by another tool. The Copilot CLI's
documentation reserves plugin-writable data for `${COPILOT_PLUGIN_DATA}`, so a flat file dropped
next to `session-store.db` had no sanctioned claim to that space and could collide with anything
the CLI chose to put there later.

## Decision

Narnia's settings database lives in a dedicated per-app folder under the platform's local
application data directory, resolved through `Environment.SpecialFolder.LocalApplicationData`:
`%LOCALAPPDATA%\narnia\settings.db` on Windows, and the XDG or `~/Library/Application Support`
equivalent elsewhere. The location is configurable through `NarniaOptions.SettingsDatabasePath`.

Narnia's access to `~/.copilot` is therefore read-only in full: session store, session state, and
installed plugins.

A startup relocator moves installations off the legacy path without user action.

## Alternatives considered

**Keep `~/.copilot/narnia-settings.db`.** No migration to write and no second location to explain.
Rejected: it writes into another tool's directory against that tool's documented guidance, and it
leaves Narnia's read-only boundary almost — but not actually — complete.

**Use `${COPILOT_PLUGIN_DATA}`.** The sanctioned location for plugin-writable data. Rejected:
Narnia runs as a standalone application and a Windows service-like background server as well as a
plugin, and that variable is only defined in the plugin-hosted case. A location that exists in
one of three launch modes cannot be the canonical one.

**Place it beside the installed application.** Rejected: the published `app/` directory is
replaced wholesale on upgrade, and per-user data does not belong in a program directory.

## Consequences

`~/.copilot` is now genuinely read-only for Narnia, which is a claim the project can make
plainly to users deciding whether to install it.

The settings database sits with Narnia's other owned state — `web-server.json`, scheduled-job
wrappers and logs, recovery packets, and the published `app/` — under one folder that is easy to
back up or delete, and it follows the same convention as the GitHub CLI, VS Code, and the
`platformdirs`/`env-paths` ecosystem.

The cost is a one-time relocation path that must be maintained until legacy installations are
gone. `SettingsDatabaseRelocator` runs before migrations on every startup and is deliberately
non-destructive: it copies rather than moves, retires the legacy file to a timestamped `.bak`,
and never deletes legacy bytes, so a failed relocation always leaves the user's data recoverable.
It is best-effort and never fatal, because a locked legacy file is an expected transient
condition rather than an error.
