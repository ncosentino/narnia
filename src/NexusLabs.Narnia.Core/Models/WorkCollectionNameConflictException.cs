namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// Indicates that a work collection could not be created or renamed because its name is already in use.
/// </summary>
public sealed class WorkCollectionNameConflictException : Exception
{
    /// <summary>
    /// Initializes a new conflict for the requested collection name.
    /// </summary>
    /// <param name="name">The conflicting collection name.</param>
    /// <param name="innerException">The underlying persistence exception.</param>
    public WorkCollectionNameConflictException(string name, Exception innerException)
        : base($"A collection named '{name}' already exists.", innerException)
    {
        Name = name;
    }

    /// <summary>Gets the conflicting collection name.</summary>
    public string Name { get; }
}
