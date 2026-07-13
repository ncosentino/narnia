Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SettingNames = [ordered]@{
    Host          = 'NARNIA_REPORT_EMAIL_SMTP_HOST'
    Port          = 'NARNIA_REPORT_EMAIL_SMTP_PORT'
    Username      = 'NARNIA_REPORT_EMAIL_SMTP_USERNAME'
    Password      = 'NARNIA_REPORT_EMAIL_SMTP_PASSWORD'
    From          = 'NARNIA_REPORT_EMAIL_FROM'
    To            = 'NARNIA_REPORT_EMAIL_TO'
    EnableSsl     = 'NARNIA_REPORT_EMAIL_ENABLE_SSL'
    TimeoutSeconds = 'NARNIA_REPORT_EMAIL_TIMEOUT_SECONDS'
}

function Remove-NarniaReportEmailOuterQuotes {
    param([AllowEmptyString()][string]$Value)

    if ($Value.Length -lt 2) {
        return $Value
    }

    $first = $Value[0]
    $last = $Value[$Value.Length - 1]
    $hasMatchingQuotes =
        ($first -eq [char]'"' -and $last -eq [char]'"') -or
        ($first -eq [char]"'" -and $last -eq [char]"'")

    if (-not $hasMatchingQuotes) {
        return $Value
    }

    return $Value.Substring(1, $Value.Length - 2)
}

function Read-NarniaReportEmailEnvironmentFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Report-email configuration file '$Path' does not exist."
    }

    $values = [ordered]@{}
    $lineNumber = 0

    foreach ($line in [System.IO.File]::ReadLines($Path)) {
        $lineNumber++
        $entry = $line.Trim()

        if ([string]::IsNullOrWhiteSpace($entry) -or $entry.StartsWith('#', [StringComparison]::Ordinal)) {
            continue
        }

        if ($entry.StartsWith('export ', [StringComparison]::OrdinalIgnoreCase)) {
            $entry = $entry.Substring(7).TrimStart()
        }

        $separatorIndex = $entry.IndexOf('=')
        if ($separatorIndex -le 0) {
            throw "Invalid report-email configuration entry at '$Path' line $lineNumber; expected KEY=VALUE."
        }

        $name = $entry.Substring(0, $separatorIndex).Trim()
        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Invalid report-email configuration key at '$Path' line $lineNumber."
        }

        $value = $entry.Substring($separatorIndex + 1).Trim()
        $values[$name] = Remove-NarniaReportEmailOuterQuotes -Value $value
    }

    return $values
}

function Get-NarniaReportEmailSetting {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$FileValues,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][string]$DefaultValue
    )

    $environmentValue = [Environment]::GetEnvironmentVariable(
        $Name,
        [EnvironmentVariableTarget]::Process)

    if ($null -ne $environmentValue) {
        return $environmentValue
    }

    if ($FileValues.Contains($Name)) {
        return [string]$FileValues[$Name]
    }

    return $DefaultValue
}

function ConvertTo-NarniaReportEmailInteger {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][int]$Minimum,
        [Parameter(Mandatory)][int]$Maximum
    )

    $parsed = 0
    if (-not [int]::TryParse($Value, [ref]$parsed) -or $parsed -lt $Minimum -or $parsed -gt $Maximum) {
        throw "Report-email configuration key '$Name' must be an integer from $Minimum through $Maximum."
    }

    return $parsed
}

function ConvertTo-NarniaReportEmailBoolean {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    switch ($Value.Trim().ToLowerInvariant()) {
        '1' { return $true }
        'true' { return $true }
        'yes' { return $true }
        'on' { return $true }
        '0' { return $false }
        'false' { return $false }
        'no' { return $false }
        'off' { return $false }
        default {
            throw "Report-email configuration key '$Name' must be true or false."
        }
    }
}

function ConvertTo-NarniaReportEmailAddresses {
    param(
        [Parameter(Mandatory)][string[]]$Values,
        [Parameter(Mandatory)][string]$SettingName
    )

    $addresses = [System.Collections.Generic.List[System.Net.Mail.MailAddress]]::new()
    $parts = @(
        $Values |
            ForEach-Object { $_ -split ';' } |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    if ($parts.Count -eq 0) {
        throw "Report-email configuration key '$SettingName' must contain at least one email address."
    }

    foreach ($part in $parts) {
        try {
            $addresses.Add([System.Net.Mail.MailAddress]::new($part))
        }
        catch {
            throw "Report-email configuration key '$SettingName' contains an invalid email address."
        }
    }

    return $addresses.ToArray()
}

function Get-NarniaReportEmailDefaultConfigPath {
    param([Parameter(Mandatory)][string]$Profile)

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw 'The local application data directory could not be resolved.'
    }

    return [System.IO.Path]::Combine(
        $localAppData,
        'narnia',
        'report-email',
        "$Profile.env")
}

function Resolve-NarniaReportEmailConfiguration {
    param(
        [Parameter(Mandatory)][string]$Profile,
        [AllowNull()][string]$ConfigPath,
        [AllowNull()][string[]]$ToOverride,
        [bool]$HasToOverride = $false
    )

    $hasExplicitConfigPath = -not [string]::IsNullOrWhiteSpace($ConfigPath)
    $resolvedConfigPath = if ($hasExplicitConfigPath) {
        [System.IO.Path]::GetFullPath($ConfigPath)
    }
    else {
        Get-NarniaReportEmailDefaultConfigPath -Profile $Profile
    }

    if ($hasExplicitConfigPath -and -not (Test-Path -LiteralPath $resolvedConfigPath -PathType Leaf)) {
        throw "Report-email configuration file '$resolvedConfigPath' does not exist."
    }

    $fileValues = if (Test-Path -LiteralPath $resolvedConfigPath -PathType Leaf) {
        Read-NarniaReportEmailEnvironmentFile -Path $resolvedConfigPath
    }
    else {
        [ordered]@{}
    }

    $hostValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.Host
    $portValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.Port -DefaultValue '587'
    $usernameValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.Username
    $passwordValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.Password
    $fromValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.From
    $toValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.To
    $enableSslValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.EnableSsl -DefaultValue 'true'
    $timeoutValue = Get-NarniaReportEmailSetting -FileValues $fileValues -Name $script:SettingNames.TimeoutSeconds -DefaultValue '30'

    $recipientValues = @(
        if ($HasToOverride) {
            $ToOverride | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($toValue)) {
            $toValue
        }
    )

    $requiredValues = [ordered]@{}
    $requiredValues[$script:SettingNames.Host] = $hostValue
    $requiredValues[$script:SettingNames.Username] = $usernameValue
    $requiredValues[$script:SettingNames.Password] = $passwordValue
    $requiredValues[$script:SettingNames.From] = $fromValue
    if ($recipientValues.Count -eq 0) {
        $requiredValues[$script:SettingNames.To] = $null
    }

    $missing = @(
        $requiredValues.GetEnumerator() |
            Where-Object { [string]::IsNullOrWhiteSpace([string]$_.Value) } |
            ForEach-Object { [string]$_.Key }
    )
    if ($missing.Count -gt 0) {
        $messageTemplate =
            "Missing required report-email configuration: {0}. Provide these values in '{1}' " +
            'or as process environment variables.'
        throw ($messageTemplate -f ($missing -join ', '), $resolvedConfigPath)
    }

    try {
        $fromAddress = [System.Net.Mail.MailAddress]::new($fromValue)
    }
    catch {
        throw "Report-email configuration key '$($script:SettingNames.From)' is not a valid email address."
    }

    return [pscustomobject]@{
        ConfigPath = $resolvedConfigPath
        Host = $hostValue
        Port = ConvertTo-NarniaReportEmailInteger `
            -Name $script:SettingNames.Port `
            -Value $portValue `
            -Minimum 1 `
            -Maximum 65535
        Username = $usernameValue
        Password = $passwordValue
        From = $fromAddress
        Recipients = @(
            ConvertTo-NarniaReportEmailAddresses `
                -Values $recipientValues `
                -SettingName $script:SettingNames.To)
        EnableSsl = ConvertTo-NarniaReportEmailBoolean `
            -Name $script:SettingNames.EnableSsl `
            -Value $enableSslValue
        TimeoutMilliseconds = 1000 * (
            ConvertTo-NarniaReportEmailInteger `
                -Name $script:SettingNames.TimeoutSeconds `
                -Value $timeoutValue `
                -Minimum 1 `
                -Maximum 300)
    }
}

function ConvertTo-NarniaReportEmailHtml {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Markdown,
        [Parameter(Mandatory)][string]$Title,
        [AllowNull()][string]$Footer
    )

    $markdownToRender = $Markdown
    if (-not [string]::IsNullOrWhiteSpace($Footer)) {
        $markdownToRender = "$markdownToRender`n`n---`n`n$Footer"
    }

    $fragment = ''
    if (-not [string]::IsNullOrEmpty($markdownToRender)) {
        $markdownCommand = Get-Command ConvertFrom-Markdown -ErrorAction SilentlyContinue
        if ($null -eq $markdownCommand) {
            throw 'ConvertFrom-Markdown is unavailable. narnia-report-email requires PowerShell 7 or later.'
        }

        $fragment = [string](ConvertFrom-Markdown -InputObject $markdownToRender).Html
    }

    $encodedTitle = [System.Net.WebUtility]::HtmlEncode($Title)
    return @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>$encodedTitle</title>
  <style>
    body { margin: 0; padding: 24px; background: #f4f6f8; color: #1f2933; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; line-height: 1.6; }
    main { max-width: 760px; margin: 0 auto; padding: 32px; background: #ffffff; border: 1px solid #d9e2ec; border-radius: 8px; }
    h1, h2, h3, h4 { color: #102a43; line-height: 1.25; }
    a { color: #0969da; }
    code { padding: 0.15em 0.35em; background: #f0f4f8; border-radius: 4px; font-family: Consolas, "Courier New", monospace; }
    pre { overflow-x: auto; padding: 16px; background: #f0f4f8; border-radius: 6px; }
    pre code { padding: 0; }
    blockquote { margin-left: 0; padding-left: 16px; color: #52606d; border-left: 4px solid #bcccdc; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 8px 10px; border: 1px solid #bcccdc; text-align: left; vertical-align: top; }
    th { background: #f0f4f8; }
    img { max-width: 100%; height: auto; }
    hr { margin: 28px 0; border: 0; border-top: 1px solid #d9e2ec; }
  </style>
</head>
<body>
  <main>
$fragment
  </main>
</body>
</html>
"@
}

function Protect-NarniaReportEmailText {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [AllowNull()][string[]]$SecretValues
    )

    $protected = $Text
    $secrets = @(
        $SecretValues |
            Where-Object { -not [string]::IsNullOrEmpty($_) } |
            Sort-Object { $_.Length } -Descending -Unique
    )

    foreach ($secret in $secrets) {
        $protected = $protected.Replace($secret, '[REDACTED]')
    }

    return $protected
}

function Resolve-NarniaReportEmailReportPath {
    param([Parameter(Mandatory)][string]$ReportPath)

    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
        throw "Markdown report '$ReportPath' does not exist."
    }

    return (Resolve-Path -LiteralPath $ReportPath).ProviderPath
}

function Resolve-NarniaReportEmailRenderPath {
    param(
        [Parameter(Mandatory)][string]$ReportPath,
        [AllowNull()][string]$RenderOutPath
    )

    $outputPath = if ([string]::IsNullOrWhiteSpace($RenderOutPath)) {
        [System.IO.Path]::Combine(
            [System.IO.Path]::GetDirectoryName($ReportPath),
            [System.IO.Path]::GetFileNameWithoutExtension($ReportPath) + '.email.html')
    }
    else {
        [System.IO.Path]::GetFullPath($RenderOutPath)
    }

    if ([string]::Equals(
            [System.IO.Path]::GetFullPath($ReportPath),
            [System.IO.Path]::GetFullPath($outputPath),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'RenderOutPath must not overwrite the Markdown report.'
    }

    return $outputPath
}

function New-NarniaReportEmailMessage {
    param(
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][string]$Html,
        [Parameter(Mandatory)][string]$Subject,
        [Parameter(Mandatory)][pscustomobject]$Configuration,
        [AllowNull()][string]$AttachmentName,
        [switch]$NoAttach
    )

    $message = [System.Net.Mail.MailMessage]::new()
    try {
        $message.From = $Configuration.From
        foreach ($recipient in $Configuration.Recipients) {
            $message.To.Add($recipient)
        }

        $message.Subject = $Subject
        $message.SubjectEncoding = [System.Text.Encoding]::UTF8
        $message.Body = $Html
        $message.BodyEncoding = [System.Text.Encoding]::UTF8
        $message.IsBodyHtml = $true

        if (-not $NoAttach) {
            $attachment = [System.Net.Mail.Attachment]::new($ReportPath)
            if (-not [string]::IsNullOrWhiteSpace($AttachmentName)) {
                $attachment.Name = $AttachmentName
            }
            $message.Attachments.Add($attachment)
        }

        return $message
    }
    catch {
        $message.Dispose()
        throw
    }
}

function Send-NarniaReportEmailMessage {
    param(
        [Parameter(Mandatory)][System.Net.Mail.MailMessage]$Message,
        [Parameter(Mandatory)][pscustomobject]$Configuration
    )

    $client = $null
    try {
        $client = [System.Net.Mail.SmtpClient]::new($Configuration.Host, $Configuration.Port)
        $client.DeliveryMethod = [System.Net.Mail.SmtpDeliveryMethod]::Network
        $client.EnableSsl = $Configuration.EnableSsl
        $client.UseDefaultCredentials = $false
        $client.Credentials = [System.Net.NetworkCredential]::new(
            $Configuration.Username,
            $Configuration.Password)
        $client.Timeout = $Configuration.TimeoutMilliseconds
        $client.Send($Message)
    }
    finally {
        if ($null -ne $client) {
            $client.Dispose()
        }
        $Message.Dispose()
    }
}

<#
.SYNOPSIS
    Renders a Markdown report as HTML and either previews it locally or sends it through SMTP.

.DESCRIPTION
    Resolves a named report-email profile from Narnia's LocalAppData folder, with process
    environment variables taking precedence over file values. Render-only mode never reads SMTP
    configuration and never opens a network connection. Delivery failures are rethrown after known
    credential values are removed from the error text.

.PARAMETER ReportPath
    Path to the Markdown report to render and optionally attach.

.PARAMETER Profile
    Configuration profile name. The default profile resolves to
    <LocalAppData>/narnia/report-email/default.env.

.PARAMETER ConfigPath
    Explicit configuration-file path. Overrides the profile-derived path for delivery.

.PARAMETER Subject
    Email subject and HTML document title. Defaults to the report file name without its extension.

.PARAMETER To
    One or more recipient addresses. Overrides NARNIA_REPORT_EMAIL_TO without exposing credentials.
    Individual values may contain semicolon-separated addresses.

.PARAMETER Footer
    Optional Markdown appended to the rendered email body. The attached report remains unchanged.

.PARAMETER AttachmentName
    Optional file name for the Markdown attachment.

.PARAMETER NoAttach
    Sends only the rendered HTML body without attaching the Markdown report.

.PARAMETER RenderOnly
    Writes the rendered HTML to disk without reading configuration or contacting SMTP.

.PARAMETER RenderOutPath
    Output path for render-only mode. Defaults to <report-name>.email.html beside the report.

.OUTPUTS
    A PSCustomObject describing the completed render or delivery without SMTP credentials.

.EXAMPLE
    Invoke-NarniaReportEmail -ReportPath '.\weekly-report.md' -Profile 'default' `
        -Subject 'Weekly engineering report'

.EXAMPLE
    Invoke-NarniaReportEmail -ReportPath '.\weekly-report.md' -RenderOnly `
        -RenderOutPath '.\weekly-report.preview.html'
#>
function Invoke-NarniaReportEmail {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$ReportPath,

        [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
        [string]$Profile = 'default',

        [AllowNull()]
        [string]$ConfigPath,

        [AllowNull()]
        [string]$Subject,

        [AllowNull()]
        [string[]]$To,

        [AllowNull()]
        [string]$Footer,

        [AllowNull()]
        [string]$AttachmentName,

        [switch]$NoAttach,

        [switch]$RenderOnly,

        [AllowNull()]
        [string]$RenderOutPath
    )

    $secretValues = @()

    try {
        if (-not $RenderOnly -and -not [string]::IsNullOrWhiteSpace($RenderOutPath)) {
            throw 'RenderOutPath can only be used with RenderOnly.'
        }
        if (-not $RenderOnly -and $NoAttach -and -not [string]::IsNullOrWhiteSpace($AttachmentName)) {
            throw 'AttachmentName cannot be used with NoAttach.'
        }
        if (-not [string]::IsNullOrWhiteSpace($AttachmentName) -and
            ($AttachmentName -match '[/\\]' -or $AttachmentName -match '[\r\n]')) {
            throw 'AttachmentName must be a file name without directory or newline characters.'
        }

        $resolvedReportPath = Resolve-NarniaReportEmailReportPath -ReportPath $ReportPath
        $resolvedSubject = if ([string]::IsNullOrWhiteSpace($Subject)) {
            [System.IO.Path]::GetFileNameWithoutExtension($resolvedReportPath)
        }
        else {
            $Subject
        }
        if ($resolvedSubject -match '[\r\n]') {
            throw 'Subject must not contain newline characters.'
        }

        $markdown = [System.IO.File]::ReadAllText($resolvedReportPath, [System.Text.Encoding]::UTF8)
        $html = ConvertTo-NarniaReportEmailHtml `
            -Markdown $markdown `
            -Title $resolvedSubject `
            -Footer $Footer

        if ($RenderOnly) {
            $resolvedRenderPath = Resolve-NarniaReportEmailRenderPath `
                -ReportPath $resolvedReportPath `
                -RenderOutPath $RenderOutPath
            $renderDirectory = [System.IO.Path]::GetDirectoryName($resolvedRenderPath)
            if (-not [string]::IsNullOrWhiteSpace($renderDirectory)) {
                [System.IO.Directory]::CreateDirectory($renderDirectory) | Out-Null
            }
            [System.IO.File]::WriteAllText(
                $resolvedRenderPath,
                $html,
                [System.Text.UTF8Encoding]::new($false))

            return [pscustomobject]@{
                Status = 'rendered'
                ReportPath = $resolvedReportPath
                HtmlPath = $resolvedRenderPath
                Subject = $resolvedSubject
            }
        }

        $configuration = Resolve-NarniaReportEmailConfiguration `
            -Profile $Profile `
            -ConfigPath $ConfigPath `
            -ToOverride $To `
            -HasToOverride:$PSBoundParameters.ContainsKey('To')
        $secretValues = @($configuration.Username, $configuration.Password)

        $message = New-NarniaReportEmailMessage `
            -ReportPath $resolvedReportPath `
            -Html $html `
            -Subject $resolvedSubject `
            -Configuration $configuration `
            -AttachmentName $AttachmentName `
            -NoAttach:$NoAttach
        Send-NarniaReportEmailMessage -Message $message -Configuration $configuration

        return [pscustomobject]@{
            Status = 'sent'
            ReportPath = $resolvedReportPath
            Profile = $Profile
            ConfigPath = $configuration.ConfigPath
            Subject = $resolvedSubject
            Recipients = @($configuration.Recipients | ForEach-Object { $_.Address })
            Attached = -not $NoAttach
        }
    }
    catch {
        $safeMessage = Protect-NarniaReportEmailText `
            -Text $_.Exception.Message `
            -SecretValues $secretValues
        throw [InvalidOperationException]::new("narnia-report-email failed: $safeMessage")
    }
}

Export-ModuleMember -Function Invoke-NarniaReportEmail
