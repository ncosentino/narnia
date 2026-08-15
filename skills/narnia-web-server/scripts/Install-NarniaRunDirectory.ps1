<#
.SYNOPSIS
    Replaces Narnia's published run directory with a staged deployment.

.DESCRIPTION
    Installs a complete staged publish by moving the current run directory aside, moving the
    staging directory into place, and restoring the previous deployment if the replacement fails.
    The source and destination must be sibling directories so the directory moves stay on one
    volume and no files from an older deployment survive.

.PARAMETER SourceDirectory
    Complete staged publish. Its directory name must begin with the run-directory name followed by
    ".staging.".

.PARAMETER RunDirectory
    Narnia's active published application directory.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceDirectory,

    [Parameter(Mandatory)]
    [string]$RunDirectory
)

$ErrorActionPreference = 'Stop'

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ([string]::Equals(
            $fullPath,
            [System.IO.Path]::GetPathRoot($fullPath),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "A filesystem root cannot be used as a Narnia deployment directory: '$fullPath'."
    }

    $fullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

$source = Get-NormalizedPath $SourceDirectory
$run = Get-NormalizedPath $RunDirectory
if ([string]::Equals($source, $run, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The staged publish and active run directory must be different.'
}

$sourceParent = Split-Path -Parent $source
$runParent = Split-Path -Parent $run
if (-not [string]::Equals(
        $sourceParent,
        $runParent,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The staged publish must be a sibling of the active run directory.'
}

$runName = Split-Path -Leaf $run
$sourceName = Split-Path -Leaf $source
if (-not $sourceName.StartsWith(
        "$runName.staging.",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "The staged directory name must begin with '$runName.staging.'."
}

if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "The staged publish directory '$source' does not exist."
}

foreach ($requiredFile in @(
    'NexusLabs.Narnia.Web.dll',
    'NexusLabs.Narnia.Web.deps.json',
    'NexusLabs.Narnia.Web.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $requiredFile) -PathType Leaf)) {
        throw "The staged publish is incomplete: '$requiredFile' is missing."
    }
}

New-Item -ItemType Directory -Path $runParent -Force | Out-Null
$backup = Join-Path $runParent "$runName.backup.$([Guid]::NewGuid().ToString('N'))"
$backupCreated = $false

try {
    if (Test-Path -LiteralPath $run -PathType Container) {
        Move-Item -LiteralPath $run -Destination $backup
        $backupCreated = $true
    }

    try {
        Move-Item -LiteralPath $source -Destination $run
    }
    catch {
        $installError = $_
        if ($backupCreated -and -not (Test-Path -LiteralPath $run)) {
            try {
                Move-Item -LiteralPath $backup -Destination $run
                $backupCreated = $false
            }
            catch {
                throw "Installing Narnia failed and the prior deployment could not be restored. " +
                    "The backup remains at '$backup'. Original error: $installError"
            }
        }

        throw $installError
    }

    if ($backupCreated) {
        try {
            Remove-Item -LiteralPath $backup -Recurse -Force
            $backupCreated = $false
        }
        catch {
            Write-Warning "Narnia was installed, but the previous deployment remains at '$backup'."
        }
    }

    [pscustomobject]@{
        RunDirectory = $run
        BackupDirectory = if ($backupCreated) { $backup } else { $null }
    }
}
finally {
    if (Test-Path -LiteralPath $source) {
        Remove-Item -LiteralPath $source -Recurse -Force
    }
}
