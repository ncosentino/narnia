$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-StagedPublish {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [string]$Marker = 'new'
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $Path 'NexusLabs.Narnia.Web.dll') -Value $Marker
    Set-Content -LiteralPath (Join-Path $Path 'NexusLabs.Narnia.Web.deps.json') -Value '{}'
    Set-Content -LiteralPath (Join-Path $Path 'NexusLabs.Narnia.Web.runtimeconfig.json') -Value '{}'
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) `
    "narnia-run-install-$([Guid]::NewGuid().ToString('N'))"
$run = Join-Path $root 'app'
$installer = Join-Path $PSScriptRoot '..\scripts\Install-NarniaRunDirectory.ps1'

try {
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $run 'old.txt') -Value 'old'
    Set-Content -LiteralPath (Join-Path $run 'hostfxr.dll') -Value 'stale'
    Set-Content -LiteralPath (Join-Path $run 'coreclr.dll') -Value 'stale'

    $staging = Join-Path $root 'app.staging.first'
    New-StagedPublish $staging
    & $installer -SourceDirectory $staging -RunDirectory $run | Out-Null

    Assert-True (Test-Path -LiteralPath (Join-Path $run 'NexusLabs.Narnia.Web.dll')) `
        'The staged deployment was not installed.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $run 'old.txt'))) `
        'A file from the previous deployment survived.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $run 'hostfxr.dll'))) `
        'A stale self-contained host survived.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $run 'coreclr.dll'))) `
        'A stale runtime survived.'

    $invalid = Join-Path $root 'app.staging.invalid'
    New-Item -ItemType Directory -Path $invalid -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $invalid 'NexusLabs.Narnia.Web.dll') -Value 'broken'
    $failed = $false
    try {
        & $installer -SourceDirectory $invalid -RunDirectory $run | Out-Null
    }
    catch {
        $failed = $true
    }

    Assert-True $failed 'An incomplete staged deployment was accepted.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $run 'NexusLabs.Narnia.Web.dll') -Raw).Trim() -eq 'new') `
        'Validation failure changed the active deployment.'

    $secondRun = Join-Path $root 'fresh-app'
    $secondStage = Join-Path $root 'fresh-app.staging.first'
    New-StagedPublish $secondStage 'fresh'
    & $installer -SourceDirectory $secondStage -RunDirectory $secondRun | Out-Null
    Assert-True (Test-Path -LiteralPath (Join-Path $secondRun 'NexusLabs.Narnia.Web.dll')) `
        'Installation into a missing run directory failed.'

    Write-Host 'Install-NarniaRunDirectory tests passed.'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
