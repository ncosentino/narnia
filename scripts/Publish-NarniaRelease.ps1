#Requires -Version 7.0

<#
.SYNOPSIS
    Builds and validates the Windows x64 Narnia release package.

.DESCRIPTION
    Publishes NexusLabs.Narnia.Web as a self-contained, untrimmed win-x64 application, stages the
    published files with the repository README and license, smoke-tests the staged executable, and
    creates narnia-win-x64.zip plus SHA256SUMS.txt.

    The smoke test runs under the ASP.NET Core "Testing" environment with isolated settings and
    session paths. It verifies the stamped /health version, a rendered UI page, static assets, and
    the Streamable HTTP MCP initialize/tools-list flow without reading or modifying the user's real
    Narnia or Copilot databases.

.PARAMETER Version
    Semantic version stamped into the release, without a leading "v" (for example,
    "0.1.0-beta.1").

.PARAMETER Root
    Narnia repository root. Defaults to the parent directory of this script's directory.

.PARAMETER OutputDirectory
    Directory that receives narnia-win-x64.zip and SHA256SUMS.txt. Temporary publish and smoke-test
    files are created beneath this directory and removed after a successful package build.

.PARAMETER SmokeTestPort
    Loopback port used by the packaged-application smoke test. Use 0 (the default) to select an
    available ephemeral port.

.OUTPUTS
    PSCustomObject describing the release archive, checksum, version, RID, and compressed size.

.EXAMPLE
    ./scripts/Publish-NarniaRelease.ps1 `
      -Version "0.1.0-beta.1" `
      -OutputDirectory "./artifacts/release"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$Root = (Split-Path -Parent $PSScriptRoot),

    [string]$OutputDirectory,

    [ValidateRange(0, 65535)]
    [int]$SmokeTestPort = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'Assert-NarniaVersion.ps1') -Version $Version | Out-Null

function Get-AvailableLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)]
        [string]$Url,

        [Parameter(Mandatory)]
        [hashtable]$Request
    )

    $response = Invoke-WebRequest `
        -Uri $Url `
        -Method Post `
        -ContentType 'application/json' `
        -Headers @{ Accept = 'application/json, text/event-stream' } `
        -Body ($Request | ConvertTo-Json -Depth 20 -Compress) `
        -UseBasicParsing `
        -TimeoutSec 15

    $dataLine = @(
        $response.Content -split '\r?\n' |
            Where-Object { $_.StartsWith('data:', [StringComparison]::OrdinalIgnoreCase) }
    ) | Select-Object -First 1

    if (-not $dataLine) {
        throw "MCP response did not contain a data event:`n$($response.Content)"
    }

    $json = $dataLine.Substring($dataLine.IndexOf(':') + 1).Trim()
    return $json | ConvertFrom-Json -Depth 100
}

function Stop-SmokeTestProcess {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory)]
        [string]$BaseUrl
    )

    if ($Process.HasExited) {
        return
    }

    try {
        Invoke-WebRequest `
            -Uri "$BaseUrl/shutdown" `
            -Method Post `
            -UseBasicParsing `
            -TimeoutSec 5 |
            Out-Null
    }
    catch {
    }

    if (-not $Process.WaitForExit(15000)) {
        Stop-Process -Id $Process.Id
        $Process.WaitForExit(5000)
    }
}

$Root = [System.IO.Path]::GetFullPath($Root)
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $Root 'artifacts\release'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$project = Join-Path $Root 'src\NexusLabs.Narnia.Web\NexusLabs.Narnia.Web.csproj'
$readme = Join-Path $Root 'README.md'
$license = Join-Path $Root 'LICENSE'
foreach ($requiredPath in @($project, $readme, $license)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release input was not found: '$requiredPath'."
    }
}

$workDirectory = Join-Path $OutputDirectory "work-$([Guid]::NewGuid().ToString('N'))"
$buildArtifactsDirectory = Join-Path $workDirectory 'build'
$publishDirectory = Join-Path $workDirectory 'publish'
$packageDirectory = Join-Path $workDirectory 'package'
$smokeDirectory = Join-Path $workDirectory 'smoke'
$archivePath = Join-Path $OutputDirectory 'narnia-win-x64.zip'
$checksumPath = Join-Path $OutputDirectory 'SHA256SUMS.txt'

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($existingAsset in @($archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $existingAsset) {
        Remove-Item -LiteralPath $existingAsset -Force
    }
}
New-Item -ItemType Directory -Path $publishDirectory, $packageDirectory, $smokeDirectory -Force |
    Out-Null

$publishArguments = @(
    'publish',
    $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--artifacts-path', $buildArtifactsDirectory,
    '-o', $publishDirectory,
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:DebugSymbols=false',
    '-p:DebugType=None',
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    '-p:IncludeSourceRevisionInInformationalVersion=false'
)

& dotnet @publishArguments 2>&1 | Out-Host
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    throw "dotnet publish failed with exit code $publishExitCode."
}

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse -Force
Copy-Item -LiteralPath $readme, $license -Destination $packageDirectory -Force

$executable = Join-Path $packageDirectory 'NexusLabs.Narnia.Web.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable was not found at '$executable'."
}

$port = if ($SmokeTestPort -eq 0) {
    Get-AvailableLoopbackPort
}
else {
    $SmokeTestPort
}
$baseUrl = "http://127.0.0.1:$port"

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executable
$startInfo.WorkingDirectory = $packageDirectory
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.ArgumentList.Add('--urls')
$startInfo.ArgumentList.Add($baseUrl)
$startInfo.Environment['ASPNETCORE_ENVIRONMENT'] = 'Testing'
$startInfo.Environment['DOTNET_ENVIRONMENT'] = 'Testing'
$startInfo.Environment['NARNIA__ConnectionString'] =
    "Data Source=$(Join-Path $smokeDirectory 'session-store.db');Mode=ReadWriteCreate"
$startInfo.Environment['NARNIA__DatabasePath'] = Join-Path $smokeDirectory 'session-store.db'
$startInfo.Environment['NARNIA__SessionStatePath'] = Join-Path $smokeDirectory 'session-state'
$startInfo.Environment['NARNIA__SettingsConnectionString'] =
    "Data Source=$(Join-Path $smokeDirectory 'settings.db');Mode=ReadWriteCreate"
$startInfo.Environment['NARNIA__SettingsDatabasePath'] = Join-Path $smokeDirectory 'settings.db'
$startInfo.Environment['NARNIA__SnapshotterEnabled'] = 'false'

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) {
    throw 'The packaged Narnia process did not start.'
}

try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    $health = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            throw "The packaged Narnia process exited early with code $($process.ExitCode)."
        }

        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 2
            break
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if ($null -eq $health) {
        throw "The packaged Narnia process did not become healthy within 60 seconds."
    }
    if ($health.status -ne 'ok') {
        throw "Unexpected /health status: '$($health.status)'."
    }
    if ($health.version -ne $Version) {
        throw "Expected /health version '$Version' but received '$($health.version)'."
    }

    $settingsPage = Invoke-WebRequest -Uri "$baseUrl/settings" -UseBasicParsing -TimeoutSec 10
    if ($settingsPage.StatusCode -ne 200 -or
        $settingsPage.Content -notmatch 'setting-copilot-command') {
        throw 'The packaged Narnia settings page did not render the expected application content.'
    }

    $styles = Invoke-WebRequest -Uri "$baseUrl/app.css" -UseBasicParsing -TimeoutSec 10
    if ($styles.StatusCode -ne 200 -or $styles.RawContentLength -lt 1000) {
        throw 'The packaged Narnia application did not serve its static CSS asset.'
    }

    $initialize = Invoke-McpRequest -Url "$baseUrl/mcp" -Request @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-03-26'
            capabilities = @{}
            clientInfo = @{
                name = 'narnia-release-smoke'
                version = '1.0'
            }
        }
    }
    if ($initialize.result.serverInfo.name -ne 'NexusLabs.Narnia.Web') {
        throw "Unexpected MCP server name: '$($initialize.result.serverInfo.name)'."
    }

    $toolList = Invoke-McpRequest -Url "$baseUrl/mcp" -Request @{
        jsonrpc = '2.0'
        id = 2
        method = 'tools/list'
        params = @{}
    }
    $toolNames = @($toolList.result.tools | ForEach-Object { $_.name })
    foreach ($requiredTool in @('list_recent_sessions', 'get_schedule_log')) {
        if ($requiredTool -notin $toolNames) {
            throw "Packaged MCP server did not expose required tool '$requiredTool'."
        }
    }
}
finally {
    Stop-SmokeTestProcess -Process $process -BaseUrl $baseUrl
    $process.Dispose()
}

Compress-Archive `
    -Path (Join-Path $packageDirectory '*') `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

$archive = Get-Item -LiteralPath $archivePath
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $($archive.Name)" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii -NoNewline

Remove-Item -LiteralPath $workDirectory -Recurse -Force

[pscustomobject]@{
    Version = $Version
    RuntimeIdentifier = 'win-x64'
    ArchivePath = $archive.FullName
    ChecksumPath = (Get-Item -LiteralPath $checksumPath).FullName
    Sha256 = $archiveHash
    SizeBytes = $archive.Length
}
