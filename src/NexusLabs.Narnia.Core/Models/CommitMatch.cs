namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A session that matched a commit-ref lookup, tagged with how confidently it was found.
/// </summary>
public sealed record CommitMatch(SessionSummary Session, CommitMatchConfidence Confidence);

/// <summary>
/// How confidently a <see cref="CommitMatch"/> was found.
/// </summary>
public enum CommitMatchConfidence
{
    /// <summary>
    /// The value only appears as text somewhere in the session's turns or checkpoints; it
    /// was never explicitly recorded as a ref for the session.
    /// </summary>
    Mentioned,

    /// <summary>The CLI explicitly recorded this value as a ref (session_refs) for the session.</summary>
    Confirmed,
}
