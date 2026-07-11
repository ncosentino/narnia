---
description: How to build Narnia from source. Covers dotnet build, dotnet publish, and runtime identifier options for each platform.
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

## Run Tests

```bash
dotnet test narnia.slnx
```

Tests live in `tests/NexusLabs.Narnia.Core.Tests/` (xUnit v3 unit tests) and `tests/NexusLabs.Narnia.Web.Tests/` (`WebApplicationFactory` integration tests); both run together in this one command.

## Publish — Web UI (serves the MCP endpoint too)

The web app is the only executable to publish — it serves the Blazor UI and the `/mcp` endpoint from the same process, so there is no separate MCP server to build:

```bash
dotnet publish src/NexusLabs.Narnia.Web -r <RID> -c Release
```

| Platform | Runtime Identifier (RID) |
|----------|--------------------------|
| Windows x64 | `win-x64` |
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| macOS x64 (Intel) | `osx-x64` |
| macOS ARM64 (Apple Silicon) | `osx-arm64` |

The published binary ends up in `src/NexusLabs.Narnia.Web/bin/Release/net10.0/<RID>/publish/`. Run the output executable directly — it starts serving both the Blazor UI and the MCP endpoint on port 5244.

!!! note "Not NativeAOT"
    Blazor Static SSR doesn't yet support NativeAOT or full trimming as of .NET 10, so this is a standard framework-dependent/self-contained JIT publish, not a single trimmed native binary. An earlier NativeAOT stdio MCP server project (`NexusLabs.Narnia.McpServer`) was removed in favor of this in-process HTTP server, so every MCP client can share one always-on instance instead of each spawning its own process.

## Development Mode

```bash
dotnet watch --project src/NexusLabs.Narnia.Web
```

Hot-reloads the Blazor UI and the MCP endpoint together — there is no second process to run alongside it.
