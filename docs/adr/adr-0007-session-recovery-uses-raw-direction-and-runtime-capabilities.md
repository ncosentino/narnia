---
description: Why recovery scans raw events for recent human direction and selects oversized-session policy from installed Copilot capabilities.
---

# ADR-0007: Session recovery uses raw direction and runtime capabilities

**Status:** Accepted

## Context

Chronicle's indexed `turns` are convenient recovery material, but they are not an authoritative
copy of an active or interrupted event stream. A direct user message can exist in `events.jsonl`
without becoming a completed indexed turn. Building a recovery packet from Chronicle alone can
therefore present an older instruction as the latest direction.

A fixed byte tail is also insufficient. Tool and model events can place a recent human message far
from the physical end of a large stream. `sessions.updated_at` cannot prove completeness because it
may advance even while the active turn remains absent from Chronicle.

Narnia also inherited a fixed character ceiling from older Copilot runtimes that loaded the entire
event stream into one V8 string. Newer Copilot packages explicitly advertise reliable resume for
very large local sessions, so a universal ceiling would force destructive recovery when native
resume is now supported.

## Decision

Recovery scans the complete event stream up to the length observed when scanning begins. The scan
uses bounded buffers, skips oversized or malformed records, and retains bounded recent messages.
Direct user messages are identified by absent `data.source`; sourced user messages are retained
separately as agent or steering evidence.

The latest normalized direct raw message is compared with selected Chronicle turns. Recovery
packets place direct raw user direction before indexed conversation and agent steering, with
independent limits so a large steering prompt cannot displace human direction.

Resume safety inspects installed Copilot package changelogs read-only. When the selected installed
version is at or newer than an explicit changelog entry advertising reliable very-large-session
resume, Narnia permits native resume. Missing, malformed, older, or non-advertising evidence keeps
the conservative legacy character-ceiling policy. Preview diagnostics expose the selected policy
and package version.

## Alternatives considered

**Use Chronicle turns only.** Fast and structured, but incomplete active turns can omit the newest
human direction and make recovery operationally incorrect.

**Read only the final fixed number of bytes.** Bounded I/O, but event volume rather than recency
determines whether a user message remains in the byte window.

**Compare raw timestamps with `sessions.updated_at`.** Cheap, but the session timestamp does not
prove every raw message was indexed.

**Always enforce the historical character ceiling.** Conservative for old runtimes, but prevents
native recovery improvements from being used and needlessly rewrites valid sessions.

**Trust package version alone.** Simpler, but couples Narnia to an inferred version threshold.
Reading explicit installed capability evidence is more defensible and fails conservatively.

## Consequences

Recovery performs a linear read of large event streams, but memory remains bounded and concurrent
appends cannot extend the scan indefinitely. Packet evidence is intentionally bounded and may omit
older or oversized records; truncation is reported.

Direct human direction and agent steering remain visibly distinct. This depends on Copilot's
recorded `data.source` provenance; absent provenance is treated as direct user input.

Capability detection depends on installed package layout and changelog wording. If either changes,
Narnia falls back to the legacy guard instead of assuming support. Native resume support can be
adopted without removing recovery for malformed starts, nested-agent histories, or older runtimes.
