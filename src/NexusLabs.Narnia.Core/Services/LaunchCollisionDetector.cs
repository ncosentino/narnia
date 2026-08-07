using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="ILaunchCollisionDetector"/>. Resolves every live session's directory with the
/// same precedence Narnia uses to launch (override path, then the session store's recorded working
/// directory, then the workspace Git root) and compares it against the pending tabs.
/// </summary>
/// <remarks>
/// This reasons about <em>recorded</em> directories, not observed process working directories.
/// A session that was launched into one directory and then had its override edited will be reported
/// against the new value, and an agent that changed directory after launch is invisible here.
/// Reading a live process's actual working directory needs per-platform memory inspection, which is
/// deliberately out of scope: the recorded value is what Narnia itself would use for a relaunch, so
/// it is the value that determines whether two Narnia launches collide.
/// </remarks>
public sealed class LaunchCollisionDetector(
    ICopilotSessionActivityReader activityReader,
    ISessionRepository sessionRepository,
    ISessionOverridesRepository overridesRepository,
    IWorkspaceReader workspaceReader,
    IFileSystem fileSystem) : ILaunchCollisionDetector
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LaunchDirectoryCollision>> DetectAsync(
        IReadOnlyList<TerminalLaunchTab> tabs,
        CancellationToken ct)
    {
        var candidates = tabs
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Directory))
            .ToArray();
        if (candidates.Length == 0)
            return [];

        var launching = new HashSet<string>(
            tabs.Select(tab => tab.SessionId),
            StringComparer.OrdinalIgnoreCase);

        var collisions = new List<LaunchDirectoryCollision>();

        // A session that is already live and is being relaunched into its own directory is not a
        // collision with itself — the user is reopening it, which the existing resume-safety and
        // operation-coordinator guards already govern.
        var occupants = await ResolveLiveOccupantsAsync(launching, ct);

        var claimed = new List<(string Directory, string SessionId, string? Name)>();
        foreach (var tab in candidates)
        {
            var directory = tab.Directory!;

            var live = occupants.FirstOrDefault(
                occupant => DirectoryPaths.AreSame(occupant.Directory, directory));
            if (live is not null)
            {
                collisions.Add(new LaunchDirectoryCollision(
                    tab.SessionId,
                    DirectoryPaths.Normalize(directory) ?? directory,
                    live.SessionId,
                    live.Name,
                    true));
                continue;
            }

            var sibling = claimed.FirstOrDefault(
                entry => DirectoryPaths.AreSame(entry.Directory, directory));
            if (sibling.SessionId is not null)
            {
                collisions.Add(new LaunchDirectoryCollision(
                    tab.SessionId,
                    DirectoryPaths.Normalize(directory) ?? directory,
                    sibling.SessionId,
                    sibling.Name,
                    false));
                continue;
            }

            claimed.Add((directory, tab.SessionId, tab.Title));
        }

        return collisions;
    }

    private async Task<List<LiveOccupant>> ResolveLiveOccupantsAsync(
        HashSet<string> launching,
        CancellationToken ct)
    {
        var occupants = new List<LiveOccupant>();
        IReadOnlySet<string> activeSessionIds;
        try
        {
            activeSessionIds = activityReader.GetActiveSessionIds();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Occupancy is advisory. Losing it must never block a launch.
            return occupants;
        }

        foreach (var sessionId in activeSessionIds)
        {
            ct.ThrowIfCancellationRequested();
            if (launching.Contains(sessionId))
                continue;

            var session = await sessionRepository.GetByIdAsync(sessionId, ct);
            var overrideRecord = await overridesRepository.GetOverrideAsync(sessionId, ct);
            var directory = ResolveDirectory(sessionId, session?.Cwd, session?.GitRoot, overrideRecord?.LocalPath);
            if (directory is null)
                continue;

            occupants.Add(new LiveOccupant(
                sessionId,
                directory,
                overrideRecord?.DisplayName ?? session?.Summary));
        }

        return occupants;
    }

    private string? ResolveDirectory(string sessionId, string? cwd, string? sessionGitRoot, string? localPath)
    {
        if (Usable(localPath))
            return localPath;
        if (Usable(cwd))
            return cwd;

        string? workspaceGitRoot = null;
        try
        {
            workspaceGitRoot = workspaceReader.ReadWorkspace(sessionId)?.GitRoot;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        var gitRoot = workspaceGitRoot ?? sessionGitRoot;
        return Usable(gitRoot) ? gitRoot : null;
    }

    private bool Usable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && fileSystem.Directory.Exists(path);

    private sealed record LiveOccupant(string SessionId, string Directory, string? Name);
}
