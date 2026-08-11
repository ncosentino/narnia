---
description: Index of Narnia's architecture decision records — significant, costly-to-reverse decisions with their context, alternatives, and consequences.
---

# Architecture Decision Records

An architecture decision record captures one significant, costly-to-reverse decision: the context
that forced it, the alternatives considered, and the consequences accepted.

Records are immutable once accepted. A decision that no longer holds is superseded by a new
record rather than edited, so the reasoning behind past choices stays readable.

Routine implementation choices, straightforward bug fixes, and decisions already covered by an
accepted record do not need an ADR.

## Records

| ID | Title | Status |
|----|-------|--------|
| [ADR-0001](adr-0001-copilot-owned-session-store-is-read-only.md) | The Copilot-owned session store is read-only for schema | Accepted |
| [ADR-0002](adr-0002-settings-database-lives-in-local-app-data.md) | Narnia's settings database lives in local application data | Accepted |

## Adding a record

Create `docs/adr/adr-NNNN-short-title.md` using the next unused number, add it to the table
above, and add it to the `Design & Decisions` section of `mkdocs.yml`.

Each record begins with YAML frontmatter containing a `description`, followed by a heading of the
form `# ADR-NNNN: Title`, a `**Status:**` line, and the sections `Context`, `Decision`,
`Alternatives considered`, and `Consequences`.

Status is one of `Proposed`, `Accepted`, or `Superseded by ADR-NNNN`.
