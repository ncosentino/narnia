namespace NexusLabs.Narnia.Core.Models;

public sealed record SessionInsights(
    int DistinctRepositories,
    int DistinctBranches,
    int TotalCheckpoints,
    int FilesCreated,
    int FilesEdited,
    int GithubHostedSessions,
    int LocalTerminalSessions);
