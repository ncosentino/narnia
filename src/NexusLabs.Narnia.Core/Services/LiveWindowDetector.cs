using System.Text.RegularExpressions;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Reconstructs open terminal windows of Copilot tabs from a process snapshot by walking
/// each <c>--resume</c>-carrying process up to its owning <c>WindowsTerminal.exe</c> and
/// grouping the resulting session ids by window. Tab titles and starting directories are
/// recovered (best-effort) from the terminal's launch command line.
/// </summary>
public sealed partial class LiveWindowDetector(IProcessSnapshotProvider processSnapshotProvider)
    : ILiveWindowDetector
{
    private const string WindowsTerminalProcessName = "WindowsTerminal.exe";
    private const int MaxAncestorWalk = 64;

    /// <inheritdoc />
    public IReadOnlyList<DetectedWindow> DetectWindows()
    {
        var processes = processSnapshotProvider.GetProcesses();

        var byId = new Dictionary<int, ProcessRecord>(processes.Count);
        foreach (var process in processes)
            byId[process.ProcessId] = process;

        var windowSessions = new Dictionary<int, List<string>>();
        foreach (var process in processes)
        {
            if (process.CommandLine is null)
                continue;

            // The owning terminal process keeps the `--resume=<id>` arguments it was launched
            // with in its OWN command line permanently — even after that tab or window is closed.
            // Counting the terminal process itself as a tab would therefore resurrect a closed
            // session forever (it never disappears until the entire terminal app exits). Only the
            // terminal's descendant shell/agent processes represent genuinely live tabs, so skip
            // the terminal process here; its launch command line is still parsed separately for
            // per-tab title/directory enrichment.
            if (string.Equals(process.Name, WindowsTerminalProcessName, StringComparison.OrdinalIgnoreCase))
                continue;

            var match = ResumeRegex().Match(process.CommandLine);
            if (!match.Success)
                continue;

            var sessionId = match.Groups[1].Value.ToLowerInvariant();
            var terminalPid = FindOwningTerminal(process, byId);
            if (terminalPid == 0)
                continue;

            if (!windowSessions.TryGetValue(terminalPid, out var sessions))
            {
                sessions = [];
                windowSessions[terminalPid] = sessions;
            }

            if (!sessions.Contains(sessionId))
                sessions.Add(sessionId);
        }

        var windows = new List<DetectedWindow>(windowSessions.Count);
        foreach (var (terminalPid, sessionIds) in windowSessions)
        {
            var launchCommandLine = byId.TryGetValue(terminalPid, out var terminal)
                ? terminal.CommandLine
                : null;
            var enrichment = ParseLaunchCommand(launchCommandLine);
            windows.Add(new DetectedWindow(terminalPid, BuildTabs(sessionIds, enrichment)));
        }

        windows.Sort(static (a, b) => a.TerminalProcessId.CompareTo(b.TerminalProcessId));
        return windows;
    }

    private static int FindOwningTerminal(ProcessRecord start, IReadOnlyDictionary<int, ProcessRecord> byId)
    {
        var current = start;
        for (var depth = 0; depth < MaxAncestorWalk; depth++)
        {
            if (string.Equals(current.Name, WindowsTerminalProcessName, StringComparison.OrdinalIgnoreCase))
                return current.ProcessId;

            if (current.ParentProcessId == 0 ||
                !byId.TryGetValue(current.ParentProcessId, out var parent) ||
                parent.ProcessId == current.ProcessId)
            {
                return 0;
            }

            current = parent;
        }

        return 0;
    }

    private static IReadOnlyList<DetectedTab> BuildTabs(
        List<string> sessionIds,
        IReadOnlyDictionary<string, LaunchTabInfo> enrichment)
    {
        var ordered = sessionIds
            .Where(enrichment.ContainsKey)
            .OrderBy(id => enrichment[id].Order)
            .Concat(sessionIds
                .Where(id => !enrichment.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal))
            .ToList();

        var tabs = new List<DetectedTab>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            enrichment.TryGetValue(ordered[i], out var info);
            tabs.Add(new DetectedTab(ordered[i], i, info?.Title, info?.Directory));
        }

        return tabs;
    }

    private static IReadOnlyDictionary<string, LaunchTabInfo> ParseLaunchCommand(string? launchCommandLine)
    {
        var result = new Dictionary<string, LaunchTabInfo>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(launchCommandLine))
            return result;

        var segments = launchCommandLine.Split("new-tab", StringSplitOptions.None);
        var order = 0;
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            var guidMatch = ResumeRegex().Match(segment);
            if (!guidMatch.Success)
                continue;

            var sessionId = guidMatch.Groups[1].Value.ToLowerInvariant();
            if (result.ContainsKey(sessionId))
                continue;

            result[sessionId] = new LaunchTabInfo(
                order++,
                ExtractValue(TitleRegex().Match(segment)),
                ExtractValue(StartingDirectoryRegex().Match(segment)));
        }

        return result;
    }

    private static string? ExtractValue(Match match)
    {
        if (!match.Success)
            return null;
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private sealed record LaunchTabInfo(int Order, string? Title, string? Directory);

    [GeneratedRegex(
        @"--resume[=\s]+([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.IgnoreCase)]
    private static partial Regex ResumeRegex();

    [GeneratedRegex(@"--title\s+(?:""([^""]*)""|(\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"--startingDirectory\s+(?:""([^""]*)""|(\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex StartingDirectoryRegex();
}
