<#
.SYNOPSIS
    Computes a stable, comparable build identity for a narnia source tree.

.DESCRIPTION
    Produces the source-identity suffix used by Get-NarniaBuildVersion.ps1. The full build version
    combines Narnia's canonical development version from Directory.Build.props with this identity,
    so /health can distinguish two builds even when both came from plugin bundles without git
    metadata:

      * Clean Git checkout (dev clone or $env:NARNIA_REPO_PATH): the short commit SHA, e.g. "git.0ef3ff203603".
      * Dirty Git checkout or plugin bundle: a deterministic SHA-256 over the 'src' content, e.g.
        "content.f1a82e159aba".
        It changes if and only if the source changes -- exactly the
        "did this update actually change anything?" signal an Update needs.

.PARAMETER Root
    Absolute path to the narnia source tree (the resolved $NARNIA_ROOT). Must contain 'src'.

.OUTPUTS
    System.String. The build identity, written to the pipeline.

.EXAMPLE
    $buildId = & ./Get-NarniaBuildId.ps1 -Root $NARNIA_ROOT
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Root
)

$buildId = $null

if (Test-Path (Join-Path $Root '.git')) {
    try {
        $changes = (& git -C $Root status --porcelain --untracked-files=normal 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not $changes) {
            $sha = (& git -C $Root rev-parse --short=12 HEAD 2>$null)
            if ($LASTEXITCODE -eq 0 -and $sha) {
                $buildId = "git.$sha"
            }
        }
    }
    catch {
    }
}

if (-not $buildId) {
    $src = Join-Path $Root 'src'
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $stream = New-Object System.IO.MemoryStream
    try {
        Get-ChildItem $src -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
            Sort-Object FullName |
            ForEach-Object {
                $relativeBytes = [System.Text.Encoding]::UTF8.GetBytes($_.FullName.Substring($Root.Length))
                $stream.Write($relativeBytes, 0, $relativeBytes.Length)
                $contentBytes = [System.IO.File]::ReadAllBytes($_.FullName)
                $stream.Write($contentBytes, 0, $contentBytes.Length)
            }
        $stream.Position = 0
        $hex = ([System.BitConverter]::ToString($sha256.ComputeHash($stream)) -replace '-', '').ToLower()
        $buildId = "content.$($hex.Substring(0, 12))"
    }
    finally {
        $stream.Dispose()
        $sha256.Dispose()
    }
}

$buildId
