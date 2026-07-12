---
description: How to build Narnia from source and produce the supported self-contained Windows x64 release package.
---

# Building from Source

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

## Clone and Build

```bash
git clone https://github.com/ncosentino/narnia.git
cd narnia
dotnet build narnia.slnx
```

Both projects (`NexusLabs.Narnia.Core`, `NexusLabs.Narnia.Web`) build together via the solution file `narnia.slnx`.

Narnia's supported prebuilt release target is **Windows x64**. The session browser and HTTP MCP
server can compile elsewhere, but terminal-window recovery, scheduled jobs, logon autostart, and
the complete launch/reopen experience are Windows integrations.

## Run Tests

```bash
dotnet test narnia.slnx
```

Tests live in `tests/NexusLabs.Narnia.Core.Tests/` (xUnit v3 unit tests) and `tests/NexusLabs.Narnia.Web.Tests/` (`WebApplicationFactory` integration tests); both run together in this one command.

## Publish the Web UI (serves the MCP endpoint too)

The web app is the only executable to publish — it serves the Blazor UI and the `/mcp` endpoint from the same process, so there is no separate MCP server to build:

```bash
dotnet publish src/NexusLabs.Narnia.Web -r win-x64 -c Release --self-contained true
```

The published application serves both the Blazor UI and the `/mcp` endpoint on port 5244.

!!! note "Not NativeAOT or trimmed"
    Blazor Static SSR doesn't support Narnia's previous NativeAOT model. The supported release is a
    self-contained JIT application: users do not need .NET installed, but the release is a ZIP of
    the application files rather than one native executable.

## Build the Release Package

Use the same reusable packager as CI and the tag-triggered release workflow:

```powershell
pwsh -File .\scripts\Publish-NarniaRelease.ps1 `
  -Version "0.1.0-beta.1" `
  -OutputDirectory .\artifacts\release
```

The script:

1. Publishes a self-contained, untrimmed `win-x64` application.
2. Stages the output with `README.md` and `LICENSE`.
3. Runs the packaged executable with isolated test databases.
4. Verifies `/health`, rendered UI, static assets, and MCP initialize/tool discovery.
5. Creates `narnia-win-x64.zip` and `SHA256SUMS.txt`.

The canonical development version lives in `Directory.Build.props`. Source/plugin builds append a
git or content identity (for example, `0.1.0-dev+git.0ef3ff203603`); release builds override the
version with the exact tag value.

The packaging script requires PowerShell 7. The packaged application itself is self-contained and
does not require PowerShell or .NET to run.

## Development Mode

```bash
dotnet watch --project src/NexusLabs.Narnia.Web
```

Hot-reloads the Blazor UI and the MCP endpoint together — there is no second process to run alongside it.
