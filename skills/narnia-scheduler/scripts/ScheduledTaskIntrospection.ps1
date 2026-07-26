$ErrorActionPreference = 'Stop'

function Get-NarniaLocalTimeFromBoundary {
    param([string]$StartBoundary)
    if ([string]::IsNullOrWhiteSpace($StartBoundary)) { return $null }

    $match = [regex]::Match($StartBoundary, 'T(\d{2}:\d{2})')
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}

function Resolve-NarniaScriptPathFromArguments {
    param([string]$Arguments)
    if ([string]::IsNullOrWhiteSpace($Arguments)) { return $null }

    $match = [regex]::Match($Arguments, '-File\s+(["''])(?<path>.*?)\1')
    if (-not $match.Success) { $match = [regex]::Match($Arguments, '-File\s+(?<path>\S+)') }
    if (-not $match.Success) { return $null }

    $path = $match.Groups['path'].Value
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        return (Resolve-Path -LiteralPath $path).Path
    }

    return $path
}
