namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// Columns available when ordering session summaries.
/// </summary>
public enum SessionSortColumn
{
    /// <summary>Sort by favorite state.</summary>
    Favorite,

    /// <summary>Sort by the displayed session name.</summary>
    Summary,

    /// <summary>Sort by the effective remote repository.</summary>
    Repository,

    /// <summary>Sort by the recorded working directory.</summary>
    Directory,

    /// <summary>Sort by conversation turn count.</summary>
    Turns,

    /// <summary>Sort by checkpoint count.</summary>
    Checkpoints,

    /// <summary>Sort by the last update timestamp.</summary>
    Updated,
}

/// <summary>
/// Directions available when ordering session summaries.
/// </summary>
public enum SessionSortDirection
{
    /// <summary>Order the selected column from low to high.</summary>
    Ascending,

    /// <summary>Order the selected column from high to low.</summary>
    Descending,
}

/// <summary>
/// Parses session sort parameters and applies deterministic session ordering.
/// </summary>
public static class SessionSummarySorting
{
    /// <summary>
    /// Parses a session sort column from its query-string representation.
    /// </summary>
    /// <param name="value">Query-string value to parse.</param>
    /// <returns>The parsed column, or <see langword="null"/> for an unknown value.</returns>
    public static SessionSortColumn? ParseColumn(string? value) => value?.ToLowerInvariant() switch
    {
        "favorite" => SessionSortColumn.Favorite,
        "name" => SessionSortColumn.Summary,
        "summary" => SessionSortColumn.Summary,
        "repository" => SessionSortColumn.Repository,
        "directory" => SessionSortColumn.Directory,
        "turns" => SessionSortColumn.Turns,
        "checkpoints" => SessionSortColumn.Checkpoints,
        "updated" => SessionSortColumn.Updated,
        _ => null,
    };

    /// <summary>
    /// Converts a session sort column to its query-string representation.
    /// </summary>
    /// <param name="column">Column to convert.</param>
    /// <returns>The stable query-string value for the column.</returns>
    public static string ToQueryValue(SessionSortColumn column) => column switch
    {
        SessionSortColumn.Favorite => "favorite",
        SessionSortColumn.Summary => "summary",
        SessionSortColumn.Repository => "repository",
        SessionSortColumn.Directory => "directory",
        SessionSortColumn.Turns => "turns",
        SessionSortColumn.Checkpoints => "checkpoints",
        SessionSortColumn.Updated => "updated",
        _ => "updated",
    };

    /// <summary>
    /// Parses a sort direction, defaulting to descending for unknown values.
    /// </summary>
    /// <param name="value">Query-string value to parse.</param>
    /// <returns>The parsed direction.</returns>
    public static SessionSortDirection ParseDirection(string? value) =>
        string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase)
            ? SessionSortDirection.Ascending
            : SessionSortDirection.Descending;

    /// <summary>
    /// Orders session summaries by the requested column and direction.
    /// </summary>
    /// <param name="sessions">Sessions to order.</param>
    /// <param name="column">Primary sort column.</param>
    /// <param name="direction">Primary sort direction.</param>
    /// <returns>A newly allocated, deterministically ordered array.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sessions"/> is <see langword="null"/>.</exception>
    public static SessionSummary[] Sort(
        IEnumerable<SessionSummary> sessions,
        SessionSortColumn column,
        SessionSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var comparer = Comparer<SessionSummary>.Create((left, right) =>
        {
            var primary = column switch
            {
                SessionSortColumn.Favorite => CompareValue(left.IsFavorite, right.IsFavorite, direction),
                SessionSortColumn.Summary => CompareText(left.Summary, right.Summary, direction),
                SessionSortColumn.Repository => CompareText(left.Repository, right.Repository, direction),
                SessionSortColumn.Directory => CompareText(left.Cwd, right.Cwd, direction),
                SessionSortColumn.Turns => CompareValue(left.TurnCount, right.TurnCount, direction),
                SessionSortColumn.Checkpoints => CompareValue(left.CheckpointCount, right.CheckpointCount, direction),
                _ => CompareValue(left.UpdatedAt, right.UpdatedAt, direction),
            };

            if (primary != 0)
                return primary;

            if (column != SessionSortColumn.Updated)
            {
                var updated = right.UpdatedAt.CompareTo(left.UpdatedAt);
                if (updated != 0)
                    return updated;
            }

            return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        });

        return sessions.OrderBy(session => session, comparer).ToArray();
    }

    private static int CompareText(
        string? left,
        string? right,
        SessionSortDirection direction)
    {
        if (left is null)
            return right is null ? 0 : 1;
        if (right is null)
            return -1;

        var result = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return direction == SessionSortDirection.Descending ? -result : result;
    }

    private static int CompareValue<T>(
        T left,
        T right,
        SessionSortDirection direction)
        where T : IComparable<T>
    {
        var result = left.CompareTo(right);
        return direction == SessionSortDirection.Descending ? -result : result;
    }
}
