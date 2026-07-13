---
name: narnia-report-email
description: >
  Render a generated Markdown report as HTML and deliver it by email through an explicitly invoked,
  deterministic PowerShell script. Use when a user asks to email a Markdown report, add explicit
  email delivery to a scheduled reporting job, preview report email formatting without sending, or
  configure a reusable SMTP notification profile.
license: MIT
compatibility: PowerShell 7 or later; SMTP delivery requires network access to a configured mail server.
metadata:
  author: nexus-labs
  version: "1.0"
allowed-tools: PowerShell(*) Read
---

# Narnia Report Email

Render a Markdown report as a self-contained HTML email, attach the original Markdown by default,
and deliver it through SMTP. This skill is always explicit: Narnia does not add email as a hidden
post-step to schedules, and this skill does not run unless the prompt or user invokes it.

## When to Use

- The user asks to email a generated Markdown report.
- A scheduled job should explicitly deliver its report after generation.
- The user wants to preview the email HTML without network activity.
- Multiple reporting jobs need one maintained Markdown-rendering and SMTP implementation.

## Design Invariants

- **Explicit orchestration only.** The calling prompt decides when to invoke this skill.
- **No nested Copilot invocation.** Run the bundled PowerShell script directly.
- **No credentials in prompts or logs.** SMTP credentials come from a profile file or process
  environment. Never ask the user to paste a password into chat.
- **Render-only is network-free.** `-RenderOnly` does not read SMTP configuration or contact a server.
- **Delivery failures are failures.** The script throws a terminating error, so a scheduled
  `copilot -p` run records the delivery failure instead of reporting false success.
- **Send every valid report.** Do not skip delivery merely because a report contains zero findings.

## Configuration Profiles

The default profile is:

```text
Windows: %LOCALAPPDATA%\narnia\report-email\default.env
Other platforms: <LocalApplicationData>/narnia/report-email/default.env
```

Another `-Profile` value selects a sibling file such as `security.env`. `-ConfigPath` overrides the
profile-derived path. A template is bundled at `config/default.env.example`.

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

Process environment variables with these names override file values. The parser splits on the first
`=` and removes one matching pair of surrounding single or double quotes. Semicolon-separate
multiple recipients.

## Invocation

The executable entry point is relative to this file:

```powershell
& "<skill-dir>/scripts/Send-NarniaReportEmail.ps1" `
    -ReportPath "C:\path\to\weekly-report.md" `
    -Profile "default" `
    -Subject "Weekly engineering report"
```

Optional delivery parameters:

- `-To <string[]>` overrides configured recipients without changing SMTP credentials.
- `-Footer <markdown>` appends content to the HTML body but not the attachment.
- `-AttachmentName <name.md>` changes the attachment file name.
- `-NoAttach` sends only the HTML body.
- `-ConfigPath <path>` uses an explicit profile file.

The script returns a structured object with `Status`, report path, subject, recipients, and
attachment state. It never returns the SMTP host, username, or password.

## Render-Only Validation

Use render-only before trusting a new template or schedule:

```powershell
& "<skill-dir>/scripts/Send-NarniaReportEmail.ps1" `
    -ReportPath "C:\path\to\weekly-report.md" `
    -Subject "Weekly engineering report" `
    -RenderOnly `
    -RenderOutPath "C:\path\to\weekly-report.preview.html"
```

If `-RenderOutPath` is omitted, the script writes `<report-name>.email.html` beside the Markdown
report. Open that file locally and inspect it before enabling delivery.

## Scheduled Job Prompt Pattern

The schedule prompt owns the complete generate-then-deliver sequence:

```text
Run the report-producing skill and write the final Markdown report to an absolute path in the
current Copilot session workspace. If report generation fails, stop and report the failure.

After the report file exists, invoke the narnia-report-email skill with that report path, profile
"default", and subject "Weekly engineering report". Send the email even when the report contains
zero findings. Treat any email-delivery error as a failed job. Do not print or request SMTP
credentials.
```

During a supervised dry run, invoke the same script with `-RenderOnly` instead of delivery. Do not
invent hidden wrapper behavior or add SMTP variables to Narnia's generated schedule script.
