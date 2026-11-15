---
applyTo: "docs/**/*.md,docs/hooks/*.py,docs/overrides/**,mkdocs.yml,scripts/test-docs-*.py"
description: Source-derived documentation descriptions and safe metadata rendering.
---

# Documentation metadata

- Give each new guide a meaningful title and an introductory prose paragraph.
  The shared hook derives descriptions; do not add routine per-page metadata.
- Preserve authored descriptions and homepage defaults. Use an explicit description
  only when an editorial override is necessary.
- Keep source metadata decoded plain text. Escape at HTML rendering boundaries;
  JSON-LD must serialize JSON before escaping less-than characters.
- Keep the metadata hook before schema and llms hooks. Preserve title verification
  and run rendered metadata regression fixtures with the hosted documentation job.

Rationale and contributor workflow: [Building from Source](../../docs/building.md#documentation-metadata).
