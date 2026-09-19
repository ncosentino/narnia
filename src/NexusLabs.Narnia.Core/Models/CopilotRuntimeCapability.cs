namespace NexusLabs.Narnia.Core.Models;

/// <summary>Whether the installed Copilot runtime advertises reliable very-large-session resume.</summary>
public enum CopilotLargeSessionResumeSupport
{
    /// <summary>Installed runtime capability could not be determined safely.</summary>
    Unknown,

    /// <summary>Installed runtime does not advertise the capability.</summary>
    Unsupported,

    /// <summary>Installed runtime explicitly advertises the capability.</summary>
    Supported,
}

/// <summary>Read-only capability evidence from the installed Copilot package.</summary>
/// <param name="Support">Detected support state.</param>
/// <param name="Version">Selected installed Copilot package version, when known.</param>
/// <param name="Evidence">Human-readable capability evidence or diagnostic.</param>
public sealed record CopilotRuntimeCapability(
    CopilotLargeSessionResumeSupport Support,
    string? Version,
    string? Evidence);
