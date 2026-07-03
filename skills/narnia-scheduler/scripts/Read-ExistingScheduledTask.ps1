<#
.SYNOPSIS
    Introspects an existing Windows scheduled task so its cadence and action can be reproduced as a
    Narnia-owned job via the create_schedule MCP tool.

.DESCRIPTION
    Narnia's create_schedule/list_schedules MCP tools only see tasks it already catalogs (or tasks
    directly in its own \Narnia\ folder). They cannot inspect an arbitrary pre-existing task
    elsewhere in Task Scheduler -- that needs a direct read of the OS scheduler. This script does
    exactly that read and nothing else: it translates the task's trigger(s) into Narnia's cadence
    vocabulary (daily/weekly/monthly) and surfaces its action (executable, arguments, working
    directory), resolving the underlying script file when the action runs one via `-File`.

    It reads the task's exported XML (`Export-ScheduledTask`) rather than the `ScheduledTasks`
    module's `.Triggers` CIM collection. The CIM collection is unreliable here: a calendar trigger
    registered via raw XML (which is how Narnia itself registers monthly jobs, since
    `New-ScheduledTaskTrigger` has no monthly option) reads back as a generic `MSFT_TaskTrigger` with
    no usable DaysOfMonth/DaysOfWeek properties, silently losing the schedule. The exported XML's
    `<ScheduleByDay>` / `<ScheduleByWeek>` / `<ScheduleByMonth>` elements are the scheduler's own
    ground truth and read back correctly regardless of how the task was originally registered.

    It does NOT read the resolved script's content or attempt to understand its logic -- that is a
    judgment call for the agent (read the file yourself with your normal file tools, understand what
    `copilot -p` prompt or skill it invokes, then design an equivalent, self-contained prompt for
    create_schedule).

    A task can have more than one trigger (e.g. separate Weekly and Monthly triggers on one task).
    This script reports ALL triggers it finds; if more than one is supported, migrate them as
    SEPARATE Narnia jobs (one create_schedule call per cadence) rather than trying to fold them into
    a single job, since a Narnia job has exactly one cadence.

.PARAMETER TaskName
    The scheduled task's name (without its folder path).

.PARAMETER TaskPath
    The Task Scheduler folder the task lives in. Defaults to the root folder ('\').

.OUTPUTS
    One PSCustomObject per trigger found (a task with 3 triggers emits 3 objects), each with:
      TaskFound, State, TriggerKind, TriggerSupported, CadenceKind, Time, Days, DayOfMonth,
      Execute, Arguments, WorkingDirectory, ResolvedScriptPath, SuggestedCadence.
    Pipe to `Format-List *` or `ConvertTo-Json -Depth 5` to inspect fully.

.EXAMPLE
    .\Read-ExistingScheduledTask.ps1 -TaskName "Example - Weekly Report" -TaskPath "\Example\"

.EXAMPLE
    .\Read-ExistingScheduledTask.ps1 -TaskName "Example - Weekly Report" -TaskPath "\Example\" |
        ConvertTo-Json -Depth 5
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TaskName,

    [string]$TaskPath = '\'
)

$ErrorActionPreference = 'Stop'

function Get-LocalTimeFromBoundary {
    param([string]$StartBoundary)
    if ([string]::IsNullOrWhiteSpace($StartBoundary)) { return $null }
    # StartBoundary is an ISO-8601 local wall-clock time, with or without a timezone offset suffix
    # (e.g. "2024-01-01T06:15:00" or "2026-07-01T06:00:00-07:00"). Either way the HH:mm right after
    # 'T' is the local fire time Narnia's cadence cares about.
    $m = [regex]::Match($StartBoundary, 'T(\d{2}:\d{2})')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

function Resolve-ScriptPathFromArguments {
    param([string]$Arguments)
    if ([string]::IsNullOrWhiteSpace($Arguments)) { return $null }
    $m = [regex]::Match($Arguments, '-File\s+"([^"]+)"')
    if (-not $m.Success) { $m = [regex]::Match($Arguments, '-File\s+(\S+)') }
    if (-not $m.Success) { return $null }
    $path = $m.Groups[1].Value
    if (Test-Path -LiteralPath $path -PathType Leaf) { return (Resolve-Path -LiteralPath $path).Path }
    return $path # surfaced even if unresolvable, so the caller can see what was attempted
}

$task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction SilentlyContinue
if (-not $task) {
    [pscustomobject]@{ TaskFound = $false; TaskName = $TaskName; TaskPath = $TaskPath }
    return
}

$info = Get-ScheduledTaskInfo -TaskName $TaskName -TaskPath $TaskPath -ErrorAction SilentlyContinue
$action = $task.Actions | Select-Object -First 1
$resolvedScript = Resolve-ScriptPathFromArguments -Arguments $action.Arguments

[xml]$xml = Export-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath
$triggerNodes = @($xml.Task.Triggers.ChildNodes)

if ($triggerNodes.Count -eq 0) {
    [pscustomobject]@{
        TaskFound = $true; State = $task.State.ToString(); TriggerKind = 'None'; TriggerSupported = $false
        CadenceKind = $null; Time = $null; Days = $null; DayOfMonth = $null
        Execute = $action.Execute; Arguments = $action.Arguments; WorkingDirectory = $action.WorkingDirectory
        ResolvedScriptPath = $resolvedScript; NextRunTime = $info.NextRunTime; LastRunTime = $info.LastRunTime
        SuggestedCadence = $null
    }
    return
}

foreach ($node in $triggerNodes) {
    $time = Get-LocalTimeFromBoundary -StartBoundary $node.StartBoundary
    $cadenceKind = $null; $days = $null; $dayOfMonth = $null; $supported = $true

    if ($node.ScheduleByDay) {
        $cadenceKind = 'Daily'
    }
    elseif ($node.ScheduleByWeek) {
        $cadenceKind = 'Weekly'
        # Day names are literal child element names (e.g. <Tuesday />, <Friday />) -- no bitmask.
        $days = @($node.ScheduleByWeek.DaysOfWeek.ChildNodes | ForEach-Object { $_.LocalName })
    }
    elseif ($node.ScheduleByMonth) {
        $cadenceKind = 'Monthly'
        $allDays = @($node.ScheduleByMonth.DaysOfMonth.Day | ForEach-Object { [int]$_ })
        $dayOfMonth = if ($allDays.Count -gt 0) { $allDays[0] } else { 1 }
        if ($allDays.Count -gt 1) {
            Write-Warning "Trigger fires on multiple days of month ($($allDays -join ',')); Narnia supports one day per job. Using day $dayOfMonth -- create a separate job per remaining day if they must all fire."
        }
    }
    else {
        # TimeTrigger (Once), BootTrigger, LogonTrigger, IdleTrigger, EventTrigger, etc. have no
        # Narnia cadence equivalent (daily/weekly/monthly-by-day-number only).
        $supported = $false
    }

    $suggestedCadence = if ($supported) {
        [ordered]@{
            cadenceKind = $cadenceKind.ToLowerInvariant()
            time = $time
            days = $days
            dayOfMonth = $dayOfMonth
            cwd = $action.WorkingDirectory
        }
    } else { $null }

    [pscustomobject]@{
        TaskFound          = $true
        State              = $task.State.ToString()
        TriggerKind        = if ($supported) { $cadenceKind } else { "Unsupported ($($node.LocalName))" }
        TriggerSupported   = $supported
        CadenceKind        = $cadenceKind
        Time               = $time
        Days               = $days
        DayOfMonth         = $dayOfMonth
        Execute            = $action.Execute
        Arguments          = $action.Arguments
        WorkingDirectory   = $action.WorkingDirectory
        ResolvedScriptPath = $resolvedScript
        NextRunTime        = $info.NextRunTime
        LastRunTime        = $info.LastRunTime
        SuggestedCadence   = $suggestedCadence
    }
}
