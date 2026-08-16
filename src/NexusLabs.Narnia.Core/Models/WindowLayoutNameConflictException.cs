namespace NexusLabs.Narnia.Core.Models;

/// <summary>Thrown when a window-layout name is already in use.</summary>
public sealed class WindowLayoutNameConflictException(
    string name,
    Exception? innerException = null)
    : Exception($"A layout named '{name}' already exists.", innerException)
{
    /// <summary>Gets the conflicting layout name.</summary>
    public string Name { get; } = name;
}
