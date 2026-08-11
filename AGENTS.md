# Agent Instructions

Narnia is an ASP.NET Core app — an MCP server and a local Blazor UI in one process — for
browsing, recovering, and scheduling GitHub Copilot CLI sessions. Windows x64 is the supported
release target.

## Sources of truth

- [docs/index.md](docs/index.md) is the documentation map. Architecture and rationale live in
  `docs/design/`; accepted decisions live in `docs/adr/` and are superseded, never edited.
- Files under `.github/instructions/` own the exact rules for the files they match. They are
  project-owned; specialize them there rather than restating rules here.
- Code, manifests, schemas, tests, and workflows are executable truth. Investigate and correct
  stale prose when sources disagree.

## Operating safeguards

- Work from evidence. Distinguish verified facts, assumptions, and material tradeoffs.
- `~/.copilot` belongs to the Copilot CLI. Narnia reads it and never writes there, and never
  changes the `session-store.db` schema. Narnia-owned state goes in its own settings database.
- Never present an overridden value as if it were what Copilot recorded; show both.
- Keep changes within the project's purpose. Surface significant architecture, trust-boundary, or
  product-scope changes before implementing them.
- This repository is public. Before publishing anything — tracked files, commit messages,
  comments, pull request text — remove local paths, machine and user names, private repository
  information, credentials, session identifiers, and live user data.

## Delivery

- Use feature branches and pull requests to `main`. Local commits are unrestricted checkpoints.
- Run the smallest targeted check while iterating: `dotnet test narnia.slnx` for code,
  `python -m mkdocs build --strict` for documentation. Release packaging and hosted evidence
  belong to CI.
- Pull request titles are conventional, single-line, and at most 72 characters, because the title
  becomes the squash commit subject.
- Before delivery, run [review-changes](.github/skills/review-changes/SKILL.md). Before marking a
  pull request ready, fix or disclose every gap, unrun check, and assumption.
- Details and required checks: [docs/design/delivery.md](docs/design/delivery.md).