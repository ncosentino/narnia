---
description: How to build Narnia from source. Covers dotnet build, dotnet publish for NativeAOT, and runtime identifier options for each platform.
---

# Building from Source

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

## Clone and Build

```bash
git clone https://github.com/ncosentino/narnia.git
cd narnia
dotnet build
```

All three projects (`NexusLabs.Narnia.Core`, `NexusLabs.Narnia.McpServer`, `NexusLabs.Narnia.Web`) build together via the solution file `narnia.slnx`.

## Run Tests

```bash
dotnet test
```

Tests live in `tests/NexusLabs.Narnia.Core.Tests/` and use xUnit v3.

## Publish — MCP Server (NativeAOT)

The MCP server supports NativeAOT publishing for a self-contained, dependency-free executable:

```bash
dotnet publish src/NexusLabs.Narnia.McpServer -r <RID> -c Release
```

| Platform | Runtime Identifier (RID) |
|----------|--------------------------|
| Windows x64 | `win-x64` |
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| macOS x64 (Intel) | `osx-x64` |
| macOS ARM64 (Apple Silicon) | `osx-arm64` |

The published binary ends up in `src/NexusLabs.Narnia.McpServer/bin/Release/net10.0/<RID>/publish/`.

## Publish — Web UI

```bash
dotnet publish src/NexusLabs.Narnia.Web -r <RID> -c Release
```

The web app is a standard ASP.NET Core Blazor Static SSR app. After publishing, run the output executable directly — it starts the Kestrel web server on port 5244.

## Development Mode

Run both components simultaneously during development:

```bash
# Terminal 1 — web UI with hot reload
dotnet watch --project src/NexusLabs.Narnia.Web

# Terminal 2 — MCP server (started by your MCP client automatically)
dotnet run --project src/NexusLabs.Narnia.McpServer
```
