<#
.SYNOPSIS
    Lists Windows Scheduled Tasks whose action appears to invoke Copilot directly or through a
    PowerShell script, so the user can select tasks for portable Narnia packaging.

.DESCRIPTION
    This helper is read-only. It enumerates Task Scheduler metadata and never reads a resolved
    wrapper script, modifies a task, or registers anything. Direct Copilot actions are marked as
    likely matches. Script-backed actions are surfaced with their resolved path for the agent to
    inspect only after the user selects them.

.PARAMETER IncludeNarnia
    Include tasks already registered in the \Narnia\ folder. They are excluded by default because
    first-class Narnia jobs should be exported through export_schedule_package instead.

.OUTPUTS
    One PSCustomObject per candidate task with task identity, state, action, working directory,
    resolved script path, trigger count, and a best-effort LikelyCopilot flag.
#>
[CmdletBinding()]
param(
    [switch]$IncludeNarnia
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ScheduledTaskIntrospection.ps1')

$tasks = Get-ScheduledTask -ErrorAction Stop
foreach ($task in $tasks) {
    if (-not $IncludeNarnia -and $task.TaskPath -eq '\Narnia\') { continue }

    $action = $task.Actions | Select-Object -First 1
    if (-not $action) { continue }

    $resolvedScript = Resolve-NarniaScriptPathFromArguments -Arguments $action.Arguments
    $actionText = "$($action.Execute) $($action.Arguments)"
    $likelyCopilot = $actionText -match '(?i)(^|[\s"''\\])(?:agency\s+)?copilot(?:\.exe)?([\s"''-]|$)'
    $isScriptAction = -not [string]::IsNullOrWhiteSpace($resolvedScript)

    if (-not $likelyCopilot -and -not $isScriptAction) { continue }

    [pscustomobject]@{
        TaskName          = $task.TaskName
        TaskPath          = $task.TaskPath
        State             = $task.State.ToString()
        Execute           = $action.Execute
        Arguments         = $action.Arguments
        WorkingDirectory  = $action.WorkingDirectory
        ResolvedScriptPath = $resolvedScript
        TriggerCount      = @($task.Triggers).Count
        LikelyCopilot     = $likelyCopilot
        RequiresScriptReview = $isScriptAction
    }
}
