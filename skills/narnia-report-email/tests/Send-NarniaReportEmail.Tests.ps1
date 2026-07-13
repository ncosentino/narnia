<#
.SYNOPSIS
    Runs dependency-free behavior tests for the narnia-report-email PowerShell implementation.

.DESCRIPTION
    Exercises configuration parsing, matching quote removal, environment precedence, Markdown
    rendering, render-only behavior, attachment construction, missing configuration, delivery
    failure exit codes, and credential redaction.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$modulePath = Join-Path $PSScriptRoot '..\scripts\NarniaReportEmail.psm1'
$entryScriptPath = Join-Path $PSScriptRoot '..\scripts\Send-NarniaReportEmail.ps1'
$module = Import-Module $modulePath -Force -PassThru
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    'narnia-report-email-tests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null

$settingNames = @(
    'NARNIA_REPORT_EMAIL_SMTP_HOST',
    'NARNIA_REPORT_EMAIL_SMTP_PORT',
    'NARNIA_REPORT_EMAIL_SMTP_USERNAME',
    'NARNIA_REPORT_EMAIL_SMTP_PASSWORD',
    'NARNIA_REPORT_EMAIL_FROM',
    'NARNIA_REPORT_EMAIL_TO',
    'NARNIA_REPORT_EMAIL_ENABLE_SSL',
    'NARNIA_REPORT_EMAIL_TIMEOUT_SECONDS'
)
$originalEnvironment = @{}
foreach ($name in $settingNames) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable(
        $name,
        [EnvironmentVariableTarget]::Process)
    Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
}

$script:Passed = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [AllowNull()][object]$Expected,
        [AllowNull()][object]$Actual,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected' but received '$Actual'."
    }
}

function Assert-ContainsText {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Expected,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "$Message Missing '$Expected'."
    }
}

function Assert-DoesNotContainText {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Unexpected,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Text.Contains($Unexpected, [StringComparison]::Ordinal)) {
        throw "$Message Found '$Unexpected'."
    }
}

function Invoke-Test {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Body
    )

    try {
        & $Body
        $script:Passed++
        Write-Host "PASS $Name"
    }
    catch {
        $failure = "$Name`: $($_.Exception.Message)"
        $script:Failures.Add($failure)
        Write-Host "FAIL $failure"
    }
}

function Write-TestConfiguration {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$HostName = 'smtp.example.com',
        [int]$Port = 587,
        [string]$Username = 'reports@example.com',
        [string]$Password = 'test-password',
        [string]$From = 'Narnia Reports <reports@example.com>',
        [string]$To = 'recipient@example.com',
        [bool]$EnableSsl = $true,
        [int]$TimeoutSeconds = 30
    )

    [System.IO.File]::WriteAllLines(
        $Path,
        @(
            "NARNIA_REPORT_EMAIL_SMTP_HOST=$HostName",
            "NARNIA_REPORT_EMAIL_SMTP_PORT=$Port",
            "NARNIA_REPORT_EMAIL_SMTP_USERNAME='$Username'",
            "NARNIA_REPORT_EMAIL_SMTP_PASSWORD=`"$Password`"",
            "NARNIA_REPORT_EMAIL_FROM='$From'",
            "NARNIA_REPORT_EMAIL_TO=$To",
            "NARNIA_REPORT_EMAIL_ENABLE_SSL=$($EnableSsl.ToString().ToLowerInvariant())",
            "NARNIA_REPORT_EMAIL_TIMEOUT_SECONDS=$TimeoutSeconds"
        ),
        [System.Text.UTF8Encoding]::new($false))
}

$reportPath = Join-Path $tempRoot 'weekly-report.md'
[System.IO.File]::WriteAllText(
    $reportPath,
    "# Weekly report`n`nA **validated** result.`n`n- First finding",
    [System.Text.UTF8Encoding]::new($false))

try {
    Invoke-Test 'parses dotenv values and removes one matching quote pair' {
        $path = Join-Path $tempRoot 'quotes.env'
        [System.IO.File]::WriteAllLines(
            $path,
            @(
                'DOUBLE="double quoted"',
                "SINGLE='single quoted'",
                'EQUALS=value=with=equals',
                'UNMATCHED="unchanged'
            ),
            [System.Text.UTF8Encoding]::new($false))

        $values = & $module {
            param($ConfigPath)
            Read-NarniaReportEmailEnvironmentFile -Path $ConfigPath
        } $path

        Assert-Equal 'double quoted' $values.DOUBLE 'Double quotes were not removed.'
        Assert-Equal 'single quoted' $values.SINGLE 'Single quotes were not removed.'
        Assert-Equal 'value=with=equals' $values.EQUALS 'The value was not split on only the first equals sign.'
        Assert-Equal '"unchanged' $values.UNMATCHED 'An unmatched quote must remain intact.'
    }

    Invoke-Test 'uses process environment values before profile values' {
        $path = Join-Path $tempRoot 'precedence.env'
        Write-TestConfiguration -Path $path
        $fileValues = & $module {
            param($ConfigPath)
            Read-NarniaReportEmailEnvironmentFile -Path $ConfigPath
        } $path
        Assert-Equal 'reports@example.com' $fileValues.NARNIA_REPORT_EMAIL_SMTP_USERNAME 'The profile username was not parsed.'
        Assert-Equal 'test-password' $fileValues.NARNIA_REPORT_EMAIL_SMTP_PASSWORD 'The profile password was not parsed.'

        [Environment]::SetEnvironmentVariable(
            'NARNIA_REPORT_EMAIL_SMTP_HOST',
            'smtp.override.example',
            [EnvironmentVariableTarget]::Process)

        try {
            $configuration = & $module {
                param($ConfigPath)
                Resolve-NarniaReportEmailConfiguration `
                    -Profile 'default' `
                    -ConfigPath $ConfigPath `
                    -ToOverride $null
            } $path

            Assert-Equal 'smtp.override.example' $configuration.Host 'The process environment did not win.'
            Assert-Equal 'test-password' $configuration.Password 'The quoted password was not parsed.'
            Assert-True $configuration.EnableSsl 'The SSL boolean was not parsed.'
            Assert-Equal 30000 $configuration.TimeoutMilliseconds 'The timeout was not converted to milliseconds.'
        }
        finally {
            Remove-Item -LiteralPath 'Env:NARNIA_REPORT_EMAIL_SMTP_HOST' -ErrorAction SilentlyContinue
        }
    }

    Invoke-Test 'rejects explicitly empty environment and recipient overrides' {
        $path = Join-Path $tempRoot 'empty-overrides.env'
        Write-TestConfiguration -Path $path

        [Environment]::SetEnvironmentVariable(
            'NARNIA_REPORT_EMAIL_SMTP_HOST',
            '',
            [EnvironmentVariableTarget]::Process)
        try {
            $hostMessage = $null
            try {
                Invoke-NarniaReportEmail -ReportPath $reportPath -ConfigPath $path | Out-Null
            }
            catch {
                $hostMessage = $_.Exception.Message
            }

            Assert-ContainsText `
                $hostMessage `
                'NARNIA_REPORT_EMAIL_SMTP_HOST' `
                'An empty environment override fell back to the profile host.'
        }
        finally {
            Remove-Item -LiteralPath 'Env:NARNIA_REPORT_EMAIL_SMTP_HOST' -ErrorAction SilentlyContinue
        }

        $recipientMessage = $null
        try {
            Invoke-NarniaReportEmail `
                -ReportPath $reportPath `
                -ConfigPath $path `
                -To '' |
                Out-Null
        }
        catch {
            $recipientMessage = $_.Exception.Message
        }

        Assert-ContainsText `
            $recipientMessage `
            'NARNIA_REPORT_EMAIL_TO' `
            'An empty recipient override fell back to the profile recipient.'
    }

    Invoke-Test 'renders Markdown and footer into a complete HTML document' {
        $html = & $module {
            ConvertTo-NarniaReportEmailHtml `
                -Markdown "# Heading`n`n**bold**`n`n- item" `
                -Title 'A <report>' `
                -Footer 'Generated by **Narnia**.'
        }

        Assert-ContainsText $html '<!doctype html>' 'The HTML document wrapper was not generated.'
        Assert-ContainsText $html '<h1 id="heading">Heading</h1>' 'The heading was not rendered.'
        Assert-ContainsText $html '<strong>bold</strong>' 'Bold Markdown was not rendered.'
        Assert-ContainsText $html '<li>item</li>' 'The list was not rendered.'
        Assert-ContainsText $html 'Generated by <strong>Narnia</strong>.' 'The footer was not rendered.'
        Assert-ContainsText $html '<title>A &lt;report&gt;</title>' 'The document title was not encoded.'
    }

    Invoke-Test 'renders without reading SMTP configuration or using the network' {
        $htmlPath = Join-Path $tempRoot 'preview\weekly-report.html'
        $result = Invoke-NarniaReportEmail `
            -ReportPath $reportPath `
            -ConfigPath (Join-Path $tempRoot 'does-not-exist.env') `
            -Subject 'Weekly engineering report' `
            -RenderOnly `
            -RenderOutPath $htmlPath

        Assert-Equal 'rendered' $result.Status 'Render-only did not return the expected status.'
        Assert-Equal ([System.IO.Path]::GetFullPath($htmlPath)) $result.HtmlPath 'The preview path was incorrect.'
        Assert-True (Test-Path -LiteralPath $htmlPath -PathType Leaf) 'Render-only did not write HTML.'
    }

    Invoke-Test 'builds the default Markdown attachment and supports NoAttach' {
        $path = Join-Path $tempRoot 'attachment.env'
        Write-TestConfiguration -Path $path
        $configuration = & $module {
            param($ConfigPath)
            Resolve-NarniaReportEmailConfiguration `
                -Profile 'default' `
                -ConfigPath $ConfigPath `
                -ToOverride $null
        } $path

        $attachedMessage = & $module {
            param($ReportPath, $Configuration)
            New-NarniaReportEmailMessage `
                -ReportPath $ReportPath `
                -Html '<p>report</p>' `
                -Subject 'Report' `
                -Configuration $Configuration `
                -AttachmentName 'engineering-report.md'
        } $reportPath $configuration

        try {
            Assert-Equal 1 $attachedMessage.Attachments.Count 'The Markdown report was not attached.'
            Assert-Equal 'engineering-report.md' $attachedMessage.Attachments[0].Name 'The attachment name was not applied.'
        }
        finally {
            $attachedMessage.Dispose()
        }

        $bodyOnlyMessage = & $module {
            param($ReportPath, $Configuration)
            New-NarniaReportEmailMessage `
                -ReportPath $ReportPath `
                -Html '<p>report</p>' `
                -Subject 'Report' `
                -Configuration $Configuration `
                -NoAttach
        } $reportPath $configuration

        try {
            Assert-Equal 0 $bodyOnlyMessage.Attachments.Count 'NoAttach still produced an attachment.'
        }
        finally {
            $bodyOnlyMessage.Dispose()
        }
    }

    Invoke-Test 'fails clearly when required configuration is missing' {
        $path = Join-Path $tempRoot 'empty.env'
        [System.IO.File]::WriteAllText($path, '', [System.Text.UTF8Encoding]::new($false))

        $message = $null
        try {
            Invoke-NarniaReportEmail -ReportPath $reportPath -ConfigPath $path | Out-Null
        }
        catch {
            $message = $_.Exception.Message
        }

        Assert-True (-not [string]::IsNullOrWhiteSpace($message)) 'Missing configuration did not fail.'
        Assert-ContainsText $message 'Missing required report-email configuration' 'The error was not actionable.'
        Assert-ContainsText $message 'NARNIA_REPORT_EMAIL_SMTP_PASSWORD' 'The missing key was not identified.'
    }

    Invoke-Test 'redacts credentials from error text' {
        $protected = & $module {
            Protect-NarniaReportEmailText `
                -Text 'Login reports@example.com failed with password test-password.' `
                -SecretValues @('reports@example.com', 'test-password')
        }

        Assert-DoesNotContainText $protected 'reports@example.com' 'The SMTP username was not redacted.'
        Assert-DoesNotContainText $protected 'test-password' 'The SMTP password was not redacted.'
        Assert-ContainsText $protected '[REDACTED]' 'The redaction marker was not emitted.'
    }

    Invoke-Test 'returns a non-zero process exit code for SMTP delivery failure without leaking credentials' {
        $path = Join-Path $tempRoot 'failure.env'
        $secret = 'do-not-leak-this-password'
        Write-TestConfiguration `
            -Path $path `
            -HostName '127.0.0.1' `
            -Port 1 `
            -Password $secret `
            -EnableSsl $false `
            -TimeoutSeconds 1

        $processInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $processInfo.FileName = (Get-Process -Id $PID).Path
        $processInfo.UseShellExecute = $false
        $processInfo.RedirectStandardOutput = $true
        $processInfo.RedirectStandardError = $true
        foreach ($argument in @(
                '-NoLogo',
                '-NoProfile',
                '-NonInteractive',
                '-File',
                $entryScriptPath,
                '-ReportPath',
                $reportPath,
                '-ConfigPath',
                $path,
                '-Subject',
                'Failure test',
                '-NoAttach')) {
            $processInfo.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::Start($processInfo)
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        $combinedOutput = "$standardOutput`n$standardError"

        Assert-True ($process.ExitCode -ne 0) 'SMTP failure returned a successful process exit code.'
        Assert-ContainsText $combinedOutput 'narnia-report-email failed' 'The delivery failure was not surfaced.'
        Assert-DoesNotContainText $combinedOutput $secret 'The SMTP password appeared in process output.'
    }
}
finally {
    foreach ($name in $settingNames) {
        if ($null -eq $originalEnvironment[$name]) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable(
                $name,
                $originalEnvironment[$name],
                [EnvironmentVariableTarget]::Process)
        }
    }

    Remove-Module $module -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:Failures.Count -gt 0) {
    throw "$($script:Failures.Count) report-email test(s) failed:`n$($script:Failures -join "`n")"
}

Write-Host "$($script:Passed) report-email tests passed."
