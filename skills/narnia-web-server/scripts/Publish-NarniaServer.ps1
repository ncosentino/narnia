<#
.SYNOPSIS
    Publishes Narnia into a clean staged directory and installs it as one deployment.

.PARAMETER Root
    Narnia source root containing the web project.

.PARAMETER RunDirectory
    Active published application directory.

.PARAMETER BuildVersion
    Optional informational version. When omitted, Get-NarniaBuildVersion.ps1 computes it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Root,

    [Parameter(Mandatory)]
    [string]$RunDirectory,

    [string]$BuildVersion
)

$ErrorActionPreference = 'Stop'

$rootPath = [System.IO.Path]::GetFullPath($Root)
$project = Join-Path $rootPath 'src\NexusLabs.Narnia.Web\NexusLabs.Narnia.Web.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Narnia web project was not found at '$project'."
}

if ([string]::IsNullOrWhiteSpace($BuildVersion)) {
    $BuildVersion = & (Join-Path $PSScriptRoot 'Get-NarniaBuildVersion.ps1') -Root $rootPath
}
if ([string]::IsNullOrWhiteSpace($BuildVersion)) {
    throw 'Could not compute the Narnia build version.'
}

$fullRunPath = [System.IO.Path]::GetFullPath($RunDirectory)
if ([string]::Equals(
        $fullRunPath,
        [System.IO.Path]::GetPathRoot($fullRunPath),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "A filesystem root cannot be used as Narnia's run directory: '$fullRunPath'."
}
$run = $fullRunPath.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$parent = Split-Path -Parent $run
$name = Split-Path -Leaf $run
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$staging = Join-Path $parent "$name.staging.$([Guid]::NewGuid().ToString('N'))"

try {
    $publishArguments = @(
        'publish',
        $project,
        '-c', 'Release',
        '-o', $staging,
        '-p:IncludeSourceRevisionInInformationalVersion=false',
        "-p:InformationalVersion=$BuildVersion")
    & dotnet @publishArguments 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $installed = & (Join-Path $PSScriptRoot 'Install-NarniaRunDirectory.ps1') `
        -SourceDirectory $staging `
        -RunDirectory $run

    [pscustomobject]@{
        Version = $BuildVersion
        RunDirectory = $installed.RunDirectory
        BackupDirectory = $installed.BackupDirectory
    }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
