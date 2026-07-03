<#
.SYNOPSIS
    Runs a scheduled job's prompt via `copilot -p`, exactly as its generated wrapper script will,
    for a supervised dry run before trusting a new or migrated schedule.

.DESCRIPTION
    A Narnia job runs as a plain `copilot -p <prompt>` with NO pre-injected environment -- whatever
    secrets its prompt/skill needs must be self-resolved (e.g. from a repo .env). The most common way
    a migrated job fails on its first real (unattended) run is that it quietly depended on an
    environment variable that a human's interactive shell happened to already have set. This script
    lets you catch that BEFORE trusting the schedule, by optionally scrubbing matching environment
    variables from the child process so the run proves self-resolution actually works, not that your
    current shell happens to have the right variables set.

    This script does NOT and CANNOT make a prompt side-effect-free by itself -- that depends entirely
    on the prompt text you pass in. For a genuine dry run, write a prompt (or temporarily edit the
    real one) that explicitly forbids the side-effecting steps, e.g. "do NOT write to the database,
    do NOT send email, do NOT git commit or push -- produce only the local draft/output file." Once
    you've verified the safe/dry version works end to end, use the real prompt for create_schedule.

.PARAMETER Prompt
    The exact prompt to pass to `copilot -p`. Include your own explicit no-side-effects guardrails
    here if this is meant to be a safe dry run.

.PARAMETER Cwd
    Working directory to run from (should match the job's intended cwd).

.PARAMETER AllowFlags
    Copilot allow-flags. Defaults to '--allow-all-tools --allow-all-paths' (matching what Narnia
    generates for a registered job).

.PARAMETER ScrubEnvPrefix
    Zero or more environment variable name prefixes (e.g. 'MYAPP_') to remove from the child
    process's environment before launching, so the run proves self-resolution rather than reuse of
    variables already set in your interactive shell. Only variable NAMES are ever printed -- never
    values.

.PARAMETER LogDir
    Directory to write the timestamped run log to. Defaults to
    "$env:LOCALAPPDATA\narnia\dry-runs".

.OUTPUTS
    Writes the live copilot output to the console and to the log file, then returns a PSCustomObject
    with ExitCode, LogPath, and ScrubbedVariables.

.EXAMPLE
    .\Invoke-NarniaDryRun.ps1 -Cwd 'C:\dev\example-blog' -ScrubEnvPrefix 'EXAMPLE_' -Prompt @'
    Generate this week's draft using the example-weekly-report skill. Do NOT write to the
    database, do NOT send email, do NOT git commit or push -- produce only the local draft file
    and print its absolute path as your final line.
    '@
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Prompt,

    [Parameter(Mandatory)]
    [string]$Cwd,

    [string]$AllowFlags = '--allow-all-tools --allow-all-paths',

    [string[]]$ScrubEnvPrefix = @(),

    [string]$LogDir = (Join-Path $env:LOCALAPPDATA 'narnia\dry-runs')
)

$ErrorActionPreference = 'Continue'

if (-not (Test-Path -LiteralPath $Cwd -PathType Container)) {
    throw "Cwd '$Cwd' does not exist."
}
if (-not (Test-Path -LiteralPath $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

$scrubbed = @()
foreach ($prefix in $ScrubEnvPrefix) {
    Get-ChildItem Env: | Where-Object { $_.Name -like "$prefix*" } | ForEach-Object {
        Remove-Item "Env:$($_.Name)" -Force
        $scrubbed += $_.Name
    }
}
if ($scrubbed.Count -gt 0) {
    Write-Host "Scrubbed $($scrubbed.Count) environment variable(s) before launch: $($scrubbed -join ', ')"
} elseif ($ScrubEnvPrefix.Count -gt 0) {
    Write-Host "No environment variables matched the given prefix(es) -- nothing to scrub (already clean, or the job never relied on them)."
}

$logPath = Join-Path $LogDir ("dry-run-" + (Get-Date -Format 'yyyy-MM-dd_HHmmss') + '.log')
Set-Content -Path $logPath -Value "=== Narnia dry run started $(Get-Date -Format o) ===`nCwd: $Cwd`nScrubbed: $($scrubbed -join ', ')`n"

Push-Location -LiteralPath $Cwd
try {
    & copilot -p $Prompt $AllowFlags.Split(' ') 2>&1 | Tee-Object -FilePath $logPath -Append
    $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
} finally {
    Pop-Location
}

Add-Content -Path $logPath -Value "`n=== Exit code: $exitCode finished $(Get-Date -Format o) ==="
Write-Host "`nDry run finished. Exit code: $exitCode. Full log: $logPath"

[pscustomobject]@{
    ExitCode          = $exitCode
    LogPath           = $logPath
    ScrubbedVariables = $scrubbed
}
