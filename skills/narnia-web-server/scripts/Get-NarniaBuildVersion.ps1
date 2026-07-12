<#
.SYNOPSIS
    Computes the complete informational version for a Narnia source build.

.DESCRIPTION
    Reads the canonical development version from Directory.Build.props through MSBuild, then
    appends the stable source identity from Get-NarniaBuildId.ps1. This keeps the semantic version
    in one source of truth while preserving the content-sensitive version comparison required by
    plugin bundles that do not contain git metadata.

.PARAMETER Root
    Absolute path to the Narnia source tree. Must contain the web project and Directory.Build.props.

.OUTPUTS
    System.String. A SemVer-compatible informational version such as
    "0.1.0-dev+git.0ef3ff203603" or "0.1.0-dev+content.f1a82e159aba".

.EXAMPLE
    $buildVersion = & ./Get-NarniaBuildVersion.ps1 -Root $NARNIA_ROOT
    dotnet publish ... -p:InformationalVersion="$buildVersion"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Root
)

$project = Join-Path $Root 'src\NexusLabs.Narnia.Web\NexusLabs.Narnia.Web.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Narnia web project was not found at '$project'."
}

$versionOutput = & dotnet msbuild $project -nologo -getProperty:Version 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Could not read Narnia's canonical version from MSBuild:`n$($versionOutput -join [Environment]::NewLine)"
}

$version = @(
    $versionOutput |
        ForEach-Object { "$_".Trim() } |
        Where-Object { $_ }
) | Select-Object -Last 1

$assertVersionScript = Join-Path $Root 'scripts\Assert-NarniaVersion.ps1'
if (-not (Test-Path -LiteralPath $assertVersionScript -PathType Leaf)) {
    throw "Narnia version validator was not found at '$assertVersionScript'."
}
$version = & $assertVersionScript -Version $version

$buildId = & (Join-Path $PSScriptRoot 'Get-NarniaBuildId.ps1') -Root $Root
if (-not $buildId) {
    throw 'Could not compute the Narnia build identity.'
}

"$version+$buildId"
