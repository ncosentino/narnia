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

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$root = Join-Path ([System.IO.Path]::GetTempPath()) `
    "narnia publish test $([Guid]::NewGuid().ToString('N'))"
$run = Join-Path $root 'app'
$publisher = Join-Path $PSScriptRoot '..\scripts\Publish-NarniaServer.ps1'

try {
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $run 'hostfxr.dll') -Value 'stale'
    Set-Content -LiteralPath (Join-Path $run 'coreclr.dll') -Value 'stale'
    Set-Content -LiteralPath (Join-Path $run 'old-deployment.txt') -Value 'stale'

    $result = & $publisher `
        -Root $repoRoot `
        -RunDirectory $run `
        -BuildVersion '0.1.0-dev+publish-test' `
        -NoRestore

    Assert-True ($result.Version -eq '0.1.0-dev+publish-test') `
        'The publisher returned the wrong version.'
    foreach ($requiredFile in @(
        'NexusLabs.Narnia.Web.exe',
        'NexusLabs.Narnia.Web.dll',
        'NexusLabs.Narnia.Web.deps.json',
        'NexusLabs.Narnia.Web.runtimeconfig.json')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $run $requiredFile) -PathType Leaf) `
            "The actual publish is missing '$requiredFile'."
    }
    foreach ($staleFile in @('hostfxr.dll', 'coreclr.dll', 'old-deployment.txt')) {
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $run $staleFile))) `
            "The stale file '$staleFile' survived the staged publish."
    }

    $runtimeOptions = (
        Get-Content -LiteralPath (Join-Path $run 'NexusLabs.Narnia.Web.runtimeconfig.json') -Raw |
            ConvertFrom-Json).runtimeOptions
    Assert-True ($null -ne $runtimeOptions.frameworks) `
        'The source publish is not marked as framework-dependent.'
    Assert-True (@(Get-ChildItem -LiteralPath $root -Directory -Filter 'app.staging.*').Count -eq 0) `
        'A staging directory remained after installation.'

    Write-Host 'Publish-NarniaServer tests passed.'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
