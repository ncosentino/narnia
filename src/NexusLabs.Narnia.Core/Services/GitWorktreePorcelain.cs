using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Parses the output of <c>git worktree list --porcelain</c>.
/// </summary>
/// <remarks>
/// Porcelain is Git's documented stable machine format: records separated by a blank line, each
/// opening with <c>worktree &lt;path&gt;</c> and followed by optional <c>HEAD</c>, <c>branch</c>,
/// <c>bare</c>, <c>detached</c>, and <c>locked</c> attribute lines. The human-readable listing is
/// explicitly not stable and is never parsed.
/// <para>
/// Kept separate from <see cref="GitWorktreeReader"/> so the format handling is a pure function that
/// can be tested against real captured output without running Git.
/// </para>
/// </remarks>
public static class GitWorktreePorcelain
{
    private const string WorktreePrefix = "worktree ";
    private const string HeadPrefix = "HEAD ";
    private const string BranchPrefix = "branch ";
    private const string RefsHeadsPrefix = "refs/heads/";

    /// <summary>Converts porcelain output into worktree records.</summary>
    /// <param name="output">Raw stdout from <c>git worktree list --porcelain</c>.</param>
    /// <param name="directoryExists">
    /// Probe for whether a worktree path is still present on disk. Injected so the parser stays
    /// free of filesystem access.
    /// </param>
    /// <returns>Worktrees in Git's own order; the first entry is the primary worktree.</returns>
    public static IReadOnlyList<GitWorktree> Parse(string output, Func<string, bool> directoryExists)
    {
        var worktrees = new List<GitWorktree>();
        if (string.IsNullOrWhiteSpace(output))
            return worktrees;

        string? path = null;
        string? head = null;
        string? branch = null;
        var isBare = false;
        var isDetached = false;

        void Flush()
        {
            if (path is null)
                return;

            var normalized = DirectoryPaths.Normalize(path) ?? path;
            worktrees.Add(new GitWorktree(
                normalized,
                branch,
                head,
                isBare,
                isDetached,
                worktrees.Count == 0,
                directoryExists(normalized)));

            path = null;
            head = null;
            branch = null;
            isBare = false;
            isDetached = false;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith(WorktreePrefix, StringComparison.Ordinal))
            {
                // A record with no trailing blank line (the final entry, or truncated output) would
                // otherwise absorb the next record's attributes.
                Flush();
                path = line[WorktreePrefix.Length..];
            }
            else if (line.StartsWith(HeadPrefix, StringComparison.Ordinal))
            {
                head = line[HeadPrefix.Length..];
            }
            else if (line.StartsWith(BranchPrefix, StringComparison.Ordinal))
            {
                var reference = line[BranchPrefix.Length..];
                branch = reference.StartsWith(RefsHeadsPrefix, StringComparison.Ordinal)
                    ? reference[RefsHeadsPrefix.Length..]
                    : reference;
            }
            else if (line == "bare")
            {
                isBare = true;
            }
            else if (line == "detached")
            {
                isDetached = true;
            }
        }

        Flush();
        return worktrees;
    }
}
