using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Persists user-owned window layouts in Narnia's settings database.</summary>
public interface IWindowLayoutsRepository
{
    /// <summary>Returns all layouts ordered alphabetically.</summary>
    ValueTask<IReadOnlyList<WindowLayout>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a layout by identifier.</summary>
    ValueTask<WindowLayout?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Creates a named layout with at least one Collection-backed slot.</summary>
    ValueTask<WindowLayout> CreateAsync(
        string name,
        IReadOnlyList<WindowLayoutMonitorDefinition> monitors,
        IReadOnlyList<WindowLayoutSlotDefinition> slots,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Replaces the complete editable monitor and window definition.</summary>
    ValueTask<bool> ReplaceDefinitionAsync(
        string id,
        IReadOnlyList<WindowLayoutMonitorDefinition> monitors,
        IReadOnlyList<WindowLayoutSlotDefinition> slots,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Renames an existing layout.</summary>
    ValueTask<bool> RenameAsync(
        string id,
        string name,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Deletes a layout and its slots without changing referenced Collections.</summary>
    ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default);
}
