namespace NexusLabs.Narnia.Core.Services;

/// <summary>Supplies process identifiers whose executable image is the Copilot runtime.</summary>
public interface ICopilotProcessProvider
{
    /// <summary>Gets currently running Copilot process identifiers.</summary>
    /// <returns>Process identifiers whose executable image is Copilot.</returns>
    IReadOnlyList<int> GetProcessIds();
}
