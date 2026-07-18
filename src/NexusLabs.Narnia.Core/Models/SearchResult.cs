namespace NexusLabs.Narnia.Core.Models;

/// <summary>Identifies the strongest name, alias, or indexed-content match for a session.</summary>
/// <param name="SessionId">The matching Copilot session identifier.</param>
/// <param name="SourceType">Structured match source such as <c>session_name</c>, <c>narnia_alias</c>, or an indexed content type.</param>
/// <param name="SourceId">Source-specific identifier when available.</param>
/// <param name="Content">The matching name, alias, or content excerpt.</param>
/// <param name="Score">Relevance score where lower values indicate stronger matches.</param>
public sealed record SearchResult(
    string SessionId,
    string? SourceType,
    string? SourceId,
    string? Content,
    double Score);
