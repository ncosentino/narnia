using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads capability evidence from installed Copilot packages without modifying them.</summary>
public interface ICopilotRuntimeCapabilityReader
{
    /// <summary>Returns the capability advertised by the selected installed Copilot package.</summary>
    CopilotRuntimeCapability Read();
}
