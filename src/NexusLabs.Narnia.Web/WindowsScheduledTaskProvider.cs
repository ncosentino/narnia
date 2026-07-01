using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Reads Windows Task Scheduler through the built-in <c>ScheduledTasks</c> PowerShell module,
/// shelling out to <c>powershell.exe</c> (always present on Windows, no extra package) and parsing
/// one compact JSON object per task. Read-only: it only queries identity and live status so the
/// Narnia registry can be joined to what the scheduler actually reports.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsScheduledTaskProvider : IScheduledTaskProvider
{
    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduledTaskStatus>> ListInFolderAsync(
        string folder, CancellationToken ct = default)
    {
        var path = NormalizeFolder(folder);
        var script =
            "Get-ScheduledTask -TaskPath '" + EscapePsLiteral(path) + "' -ErrorAction SilentlyContinue | " +
            "ForEach-Object { " + ProjectTaskScript + " }";

        return await RunAsync(script, ct);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledTaskStatus?> GetAsync(
        string folder, string name, CancellationToken ct = default)
    {
        var path = NormalizeFolder(folder);
        var script =
            "Get-ScheduledTask -TaskPath '" + EscapePsLiteral(path) + "' -TaskName '" + EscapePsLiteral(name) + "' -ErrorAction SilentlyContinue | " +
            "ForEach-Object { " + ProjectTaskScript + " }";

        var results = await RunAsync(script, ct);
        return results.Count > 0 ? results[0] : null;
    }

    // Projects a ScheduledTask + its info into one compact JSON line. Emitting JSON Lines (one
    // object per line) avoids Windows PowerShell 5.1's single-element-array unwrapping quirk.
    private const string ProjectTaskScript =
        """
        $info = $_ | Get-ScheduledTaskInfo;
        $action = (($_.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)" }) -join ' || ');
        $last = if ($info.LastRunTime -and $info.LastRunTime.Year -ge 2000) { $info.LastRunTime.ToString('o') } else { $null };
        $next = if ($info.NextRunTime -and $info.NextRunTime.Year -ge 2000) { $info.NextRunTime.ToString('o') } else { $null };
        ([pscustomobject]@{ folder = "$($_.TaskPath)"; name = "$($_.TaskName)"; state = "$($_.State)"; lastRunTime = $last; lastResult = $info.LastTaskResult; nextRunTime = $next; action = $action } | ConvertTo-Json -Compress)
        """;

    private static async ValueTask<IReadOnlyList<ScheduledTaskStatus>> RunAsync(
        string script, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi);
        if (process is null)
            return [];

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return ScheduledTaskStatusJson.ParseLines(stdout);
    }

    private static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return @"\";
        var trimmed = folder.Trim();
        if (!trimmed.StartsWith('\\'))
            trimmed = "\\" + trimmed;
        if (!trimmed.EndsWith('\\'))
            trimmed += "\\";
        return trimmed;
    }

    private static string EscapePsLiteral(string value) => value.Replace("'", "''");
}

/// <summary>
/// No-op scheduled-task provider for platforms without a supported OS scheduler integration.
/// </summary>
public sealed class UnsupportedScheduledTaskProvider : IScheduledTaskProvider
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ScheduledTaskStatus>> ListInFolderAsync(
        string folder, CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<ScheduledTaskStatus>>([]);

    /// <inheritdoc />
    public ValueTask<ScheduledTaskStatus?> GetAsync(
        string folder, string name, CancellationToken ct = default) =>
        ValueTask.FromResult<ScheduledTaskStatus?>(null);
}
