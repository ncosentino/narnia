---
description: Step-by-step guide to running Narnia's MCP server and web UI. Get session search working in GitHub Copilot CLI in under five minutes.
---

# Getting Started

## Prerequisites

- Windows x64 for the supported prebuilt release and complete feature set
- GitHub Copilot CLI with at least one session in `~/.copilot/session-store.db`
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) only for source/plugin builds

## Step 1: Install Narnia

Tagged beta releases are published on the [GitHub Releases page](https://github.com/ncosentino/narnia/releases). Download:

- `narnia-win-x64.zip`
- `SHA256SUMS.txt`

Verify the archive and install it into Narnia's application directory. If Narnia is already
installed, stop the server first. Replace the directory rather than extracting over it: release
builds are self-contained, while plugin/source builds are framework-dependent, and mixing their
files produces an invalid deployment.

```powershell
$expected = ((Get-Content .\SHA256SUMS.txt) -split '\s+')[0]
$actual = (Get-FileHash .\narnia-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "Narnia release checksum mismatch." }

$runDir = Join-Path $env:LOCALAPPDATA 'narnia\app'
if (Test-Path -LiteralPath $runDir) {
  Remove-Item -LiteralPath $runDir -Recurse -Force
}
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
Expand-Archive .\narnia-win-x64.zip -DestinationPath $runDir -Force
```

The release is self-contained; no .NET runtime or SDK is required. Release executables are currently unsigned,
so Windows SmartScreen may identify the publisher as unknown. Verify the checksum before running
the downloaded application.

!!! info "Rolling plugin/source channel"
    `copilot plugin install ncosentino/narnia` installs the latest repository source, skills, hook,
    and MCP configuration. Its `narnia-web-server` skill builds from source and therefore requires
    the .NET 10 SDK. GitHub Releases are the prebuilt application channel; direct plugin installs
    continue tracking `main`.

## Step 2: Start the Server

Narnia is a single process: the web UI and MCP server are the same thing. Start the downloaded
release:

```powershell
$runDir = Join-Path $env:LOCALAPPDATA 'narnia\app'
Start-Process (Join-Path $runDir 'NexusLabs.Narnia.Web.exe') `
  -ArgumentList '--urls','http://127.0.0.1:5244' `
  -WorkingDirectory $runDir `
  -WindowStyle Hidden
```

This serves the Blazor web UI at `http://localhost:5244` **and** the MCP endpoint at `http://localhost:5244/mcp`. Leave it running — every step below depends on it.

If you use the rolling plugin/source channel instead, ask your LLM to start Narnia with the
[`narnia-web-server` skill](skills/narnia-web-server.md).

## Step 3: Configure Your MCP Client

Point your MCP client at the running server's `/mcp` endpoint. See [Setup by Tool](setup-by-tool.md) for exact snippets per client.

For **GitHub Copilot CLI**, installing Narnia as a plugin ships a working `.mcp.json` automatically:

```json
{
  "mcpServers": {
    "narnia": {
      "type": "http",
      "url": "http://127.0.0.1:5244/mcp",
      "tools": ["*"]
    }
  }
}
```

There's nothing to hand-edit for that path — see [Setup by Tool](setup-by-tool.md#github-copilot-cli).

## Step 4: Verify the Tools Are Available

Start a new Copilot CLI session (or reconnect your MCP client so it picks up the server). Ask it:

> "List my most recent Narnia sessions"

or

> "Find sessions where I was working on the macerus project"

You should see results from your local session history.

## Updating a Downloaded Release

Stop the running server gracefully, wait for it to exit, then extract the new ZIP over the same
application directory:

```powershell
$statePath = Join-Path $env:LOCALAPPDATA 'narnia\web-server.json'
$state = if (Test-Path $statePath) {
  Get-Content $statePath -Raw | ConvertFrom-Json
}

if ($state) {
  $process = Get-Process -Id $state.Pid -ErrorAction SilentlyContinue
  if ($process) {
    if ($process.Path -ne $state.ExePath) {
      throw "Narnia's run-state PID belongs to a different executable; stop Narnia manually."
    }

    try {
      Invoke-WebRequest "$($state.Url)/shutdown" -Method Post -UseBasicParsing | Out-Null
    } catch {
    }

    $stopped = $false
    for ($i = 0; $i -lt 120; $i++) {
      if (-not (Get-Process -Id $state.Pid -ErrorAction SilentlyContinue)) {
        $stopped = $true
        break
      }
      Start-Sleep -Milliseconds 500
    }
    if (-not $stopped) { throw "Narnia did not exit; release files were not replaced." }
  } elseif (Get-NetTCPConnection -LocalPort ([Uri]$state.Url).Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Narnia's run-state file is stale while its port is active; stop the process manually."
  }
} else {
  if (Get-NetTCPConnection -LocalPort 5244 -State Listen -ErrorAction SilentlyContinue) {
    throw "Port 5244 is active without a Narnia run-state file; stop the process manually."
  }
}

$runDir = Join-Path $env:LOCALAPPDATA 'narnia\app'
Remove-Item $runDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
Expand-Archive .\narnia-win-x64.zip `
  -DestinationPath $runDir `
  -Force
```

Start the executable again using Step 2. Narnia's settings database remains outside the application
directory, so replacing release files does not replace saved settings, schedules, or window
history.

## Next Steps

- [Setup by Tool](setup-by-tool.md) — per-client configuration snippets
- [Configuration](configuration.md) — environment variable reference
- [MCP Tools](tools/index.md) — full tool reference
