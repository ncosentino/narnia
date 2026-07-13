---
description: Render Markdown reports as HTML, preview them without network activity, and explicitly deliver them through a reusable SMTP profile.
---

# narnia-report-email

`narnia-report-email` renders a generated Markdown report as a self-contained HTML email and
attaches the original Markdown by default. It is an explicit workflow step: existing schedules do
not send anything unless their prompt invokes this skill.

## Capabilities

| Capability | Behavior |
|------------|----------|
| **HTML rendering** | Uses PowerShell 7's Markdown renderer and wraps the result in email-friendly styling |
| **Markdown attachment** | Attaches the original report by default; `-NoAttach` suppresses it |
| **Reusable profiles** | Resolves SMTP settings from Narnia's LocalAppData folder, outside consumer repositories |
| **Environment overrides** | Process environment variables override profile values |
| **Render-only validation** | Writes inspectable HTML without reading SMTP configuration or using the network |
| **Failure propagation** | Throws on invalid configuration or SMTP failure so a scheduled run cannot report false success |

## Configure a Profile

The `default` profile resolves to:

```text
Windows: %LOCALAPPDATA%\narnia\report-email\default.env
Other platforms: <LocalApplicationData>/narnia/report-email/default.env
```

Copy the bundled `skills/narnia-report-email/config/default.env.example` template to that location
and edit it locally. Do not paste SMTP credentials into a Copilot prompt.

| Setting | Required | Default |
|---------|----------|---------|
| `NARNIA_REPORT_EMAIL_SMTP_HOST` | Yes | — |
| `NARNIA_REPORT_EMAIL_SMTP_PORT` | No | `587` |
| `NARNIA_REPORT_EMAIL_SMTP_USERNAME` | Yes | — |
| `NARNIA_REPORT_EMAIL_SMTP_PASSWORD` | Yes | — |
| `NARNIA_REPORT_EMAIL_FROM` | Yes | — |
| `NARNIA_REPORT_EMAIL_TO` | Yes, unless `-To` is supplied | — |
| `NARNIA_REPORT_EMAIL_ENABLE_SSL` | No | `true` |
| `NARNIA_REPORT_EMAIL_TIMEOUT_SECONDS` | No | `30` |

The parser splits each non-comment line on the first `=` and removes one matching pair of
surrounding single or double quotes. Separate multiple recipients with semicolons. A different
`-Profile` selects a sibling file such as `security.env`; `-ConfigPath` selects an explicit file.

## Preview Without Sending

Render-only mode requires no profile and makes no network request:

```powershell
& "<skill-dir>/scripts/Send-NarniaReportEmail.ps1" `
    -ReportPath "C:\path\to\weekly-report.md" `
    -Subject "Weekly engineering report" `
    -RenderOnly `
    -RenderOutPath "C:\path\to\weekly-report.preview.html"
```

When `-RenderOutPath` is omitted, the HTML is written beside the report as
`<report-name>.email.html`.

## Deliver a Report

```powershell
& "<skill-dir>/scripts/Send-NarniaReportEmail.ps1" `
    -ReportPath "C:\path\to\weekly-report.md" `
    -Profile "default" `
    -Subject "Weekly engineering report"
```

Optional parameters:

- `-To <string[]>` overrides the configured recipients.
- `-Footer <markdown>` appends content to the HTML body without changing the attachment.
- `-AttachmentName <name.md>` changes the attachment file name.
- `-NoAttach` sends only the HTML body.
- `-ConfigPath <path>` overrides the profile-derived path.

Successful invocations return structured render or delivery metadata without SMTP credentials.
Missing configuration and delivery failures produce terminating errors. Known credential values
are removed from error text before it is surfaced.

## Complete Scheduled-Job Prompt

The [narnia-scheduler skill](narnia-scheduler.md) stores the prompt as the job's complete behavior.
Use an explicit generate-then-deliver sequence:

```text
Run the report-producing skill and write the final Markdown report to an absolute path in the
current Copilot session workspace. If report generation fails, stop and report the failure.

After the report file exists, invoke the narnia-report-email skill with that report path, profile
"default", and subject "Weekly engineering report". Send the email even when the report contains
zero findings. Treat any email-delivery error as a failed job. Do not print or request SMTP
credentials.
```

For a supervised dry run, keep the generation step and invoke `narnia-report-email` with
`-RenderOnly`. Email is not an implicit Narnia post-step, and no SMTP values are added to the
generated schedule wrapper.
