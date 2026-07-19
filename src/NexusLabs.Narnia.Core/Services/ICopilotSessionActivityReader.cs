namespace NexusLabs.Narnia.Core.Services;

/// <summary>Finds every session currently owned by a live Copilot runtime process.</summary>
public interface ICopilotSessionActivityReader
{
    /// <summary>Gets active local session identifiers from verified process and lock signals.</summary>
    /// <returns>Case-insensitive set of active session identifiers.</returns>
    IReadOnlySet<string> GetActiveSessionIds();
}
