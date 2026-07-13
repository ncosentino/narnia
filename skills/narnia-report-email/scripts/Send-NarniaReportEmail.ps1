<#
.SYNOPSIS
    Renders a Markdown report as HTML and either previews it locally or sends it through SMTP.

.DESCRIPTION
    Entry point for the narnia-report-email skill. Delivery resolves a named configuration profile
    from Narnia's LocalAppData folder unless ConfigPath is supplied. RenderOnly performs no
    configuration lookup and no network activity.

.PARAMETER ReportPath
    Path to the Markdown report to render and optionally attach.

.PARAMETER Profile
    Configuration profile name. Defaults to "default".

.PARAMETER ConfigPath
    Explicit configuration-file path for delivery.

.PARAMETER Subject
    Email subject and HTML document title.

.PARAMETER To
    One or more recipient addresses that override the configured recipients.

.PARAMETER Footer
    Optional Markdown appended to the email body without modifying the report attachment.

.PARAMETER AttachmentName
    Optional file name for the Markdown attachment.

.PARAMETER NoAttach
    Suppresses the Markdown attachment.

.PARAMETER RenderOnly
    Writes HTML without reading SMTP configuration or making a network request.

.PARAMETER RenderOutPath
    Output path for render-only HTML.

.OUTPUTS
    A PSCustomObject describing the completed render or delivery without SMTP credentials.

.EXAMPLE
    .\Send-NarniaReportEmail.ps1 -ReportPath '.\weekly-report.md' `
        -Subject 'Weekly engineering report'

.EXAMPLE
    .\Send-NarniaReportEmail.ps1 -ReportPath '.\weekly-report.md' -RenderOnly `
        -RenderOutPath '.\weekly-report.preview.html'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ReportPath,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Profile = 'default',

    [string]$ConfigPath,

    [string]$Subject,

    [string[]]$To,

    [string]$Footer,

    [string]$AttachmentName,

    [switch]$NoAttach,

    [switch]$RenderOnly,

    [string]$RenderOutPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NarniaReportEmail.psm1') -Force

Invoke-NarniaReportEmail @PSBoundParameters
