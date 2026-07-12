<#
.SYNOPSIS
    Validates a Narnia semantic version.

.DESCRIPTION
    Accepts SemVer 2.0 core and prerelease versions without build metadata. Numeric identifiers
    must not contain leading zeroes. The validated version is written back to the pipeline so
    workflows can reuse it directly.

.PARAMETER Version
    Version without a leading "v", such as "0.1.0", "0.1.0-dev", or "0.1.0-beta.1".

.OUTPUTS
    System.String. The validated version.

.EXAMPLE
    $version = ./scripts/Assert-NarniaVersion.ps1 -Version "0.1.0-beta.1"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version
)

$numericIdentifier = '(?:0|[1-9]\d*)'
$alphanumericIdentifier = '(?:[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)'
$prereleaseIdentifier = "(?:$numericIdentifier|$alphanumericIdentifier)"
$pattern = "^(?:$numericIdentifier)\.(?:$numericIdentifier)\.(?:$numericIdentifier)" +
    "(?:-$prereleaseIdentifier(?:\.$prereleaseIdentifier)*)?$"

if ($Version -notmatch $pattern) {
    throw "Version '$Version' is not a supported semantic version."
}

$Version
