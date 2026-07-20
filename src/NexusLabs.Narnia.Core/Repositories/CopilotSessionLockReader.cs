using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Filesystem implementation for reading Copilot's process-specific session locks.</summary>
public sealed class CopilotSessionLockReader(
    NarniaOptions options,
    IFileSystem fileSystem) : ICopilotSessionLockReader
{
    /// <inheritdoc />
    public IReadOnlyList<string> GetSessionIds(int copilotProcessId) =>
        GetSessionIdsByProcess([copilotProcessId])
            .GetValueOrDefault(copilotProcessId, []);

    /// <inheritdoc />
    public IReadOnlyDictionary<int, IReadOnlyList<string>> GetSessionIdsByProcess(
        IReadOnlyCollection<int> copilotProcessIds)
    {
        var requested = copilotProcessIds.ToHashSet();
        if (requested.Count == 0 ||
            !fileSystem.Directory.Exists(options.SessionStatePath))
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        string[] sessionDirectories;
        try
        {
            sessionDirectories = fileSystem.Directory.GetDirectories(
                options.SessionStatePath,
                "*",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var grouped = new Dictionary<int, HashSet<string>>();
        foreach (var sessionDirectory in sessionDirectories)
        {
            string[] matches;
            try
            {
                var attributes = fileSystem.File.GetAttributes(sessionDirectory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                matches = fileSystem.Directory.GetFiles(
                    sessionDirectory,
                    "inuse.*.lock",
                    SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var sessionId = fileSystem.Path.GetFileName(sessionDirectory);
            if (string.IsNullOrWhiteSpace(sessionId))
                continue;

            foreach (var match in matches)
            {
                if (!TryParseProcessId(fileSystem.Path.GetFileName(match), out var processId) ||
                    !requested.Contains(processId))
                {
                    continue;
                }

                if (!grouped.TryGetValue(processId, out var sessionIds))
                {
                    sessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    grouped[processId] = sessionIds;
                }
                sessionIds.Add(sessionId);
            }
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static bool TryParseProcessId(string fileName, out int processId)
    {
        processId = 0;
        const string prefix = "inuse.";
        const string suffix = ".lock";
        return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(
                   fileName[prefix.Length..^suffix.Length],
                   out processId);
    }
}
