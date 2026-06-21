namespace NexusLabs.Narnia.Core.Models;

public enum SessionSortColumn
{
    Summary,
    Repository,
    Directory,
    Turns,
    Checkpoints,
    Updated,
}

public enum SessionSortDirection
{
    Ascending,
    Descending,
}

public static class SessionSummarySorting
{
    public static SessionSortColumn? ParseColumn(string? value) => value?.ToLowerInvariant() switch
    {
        "summary" => SessionSortColumn.Summary,
        "repository" => SessionSortColumn.Repository,
        "directory" => SessionSortColumn.Directory,
        "turns" => SessionSortColumn.Turns,
        "checkpoints" => SessionSortColumn.Checkpoints,
        "updated" => SessionSortColumn.Updated,
        _ => null,
    };

    public static string ToQueryValue(SessionSortColumn column) => column switch
    {
        SessionSortColumn.Summary => "summary",
        SessionSortColumn.Repository => "repository",
        SessionSortColumn.Directory => "directory",
        SessionSortColumn.Turns => "turns",
        SessionSortColumn.Checkpoints => "checkpoints",
        SessionSortColumn.Updated => "updated",
        _ => "updated",
    };

    public static SessionSortDirection ParseDirection(string? value) =>
        string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase)
            ? SessionSortDirection.Ascending
            : SessionSortDirection.Descending;

    public static SessionSummary[] Sort(
        IEnumerable<SessionSummary> sessions,
        SessionSortColumn column,
        SessionSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        IComparer<SessionSummary> comparer = column switch
        {
            SessionSortColumn.Summary => Comparer<SessionSummary>.Create(
                (a, b) => string.Compare(a.Summary, b.Summary, StringComparison.OrdinalIgnoreCase)),
            SessionSortColumn.Repository => Comparer<SessionSummary>.Create(
                (a, b) => string.Compare(a.Repository, b.Repository, StringComparison.OrdinalIgnoreCase)),
            SessionSortColumn.Directory => Comparer<SessionSummary>.Create(
                (a, b) => string.Compare(a.Cwd, b.Cwd, StringComparison.OrdinalIgnoreCase)),
            SessionSortColumn.Turns => Comparer<SessionSummary>.Create(
                (a, b) => a.TurnCount.CompareTo(b.TurnCount)),
            SessionSortColumn.Checkpoints => Comparer<SessionSummary>.Create(
                (a, b) => a.CheckpointCount.CompareTo(b.CheckpointCount)),
            _ => Comparer<SessionSummary>.Create(
                (a, b) => a.UpdatedAt.CompareTo(b.UpdatedAt)),
        };

        return direction == SessionSortDirection.Descending
            ? sessions.OrderByDescending(s => s, comparer).ToArray()
            : sessions.OrderBy(s => s, comparer).ToArray();
    }
}
