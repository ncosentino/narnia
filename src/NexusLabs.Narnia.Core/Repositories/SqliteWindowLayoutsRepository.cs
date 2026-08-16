using System.Text;
using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Stores persisted window layouts in Narnia's SQLite settings database.</summary>
public sealed class SqliteWindowLayoutsRepository(NarniaOptions options)
    : IWindowLayoutsRepository
{
    private const string LayoutColumns = "id, name, created_at, updated_at";

    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WindowLayout>> GetAllAsync(
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {LayoutColumns} FROM window_layouts ORDER BY name_key, id";
        return await ReadLayoutsAsync(connection, command, ct);
    }

    /// <inheritdoc />
    public async ValueTask<WindowLayout?> GetByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {LayoutColumns} FROM window_layouts WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        var layouts = await ReadLayoutsAsync(connection, command, ct);
        return layouts.Count == 0 ? null : layouts[0];
    }

    /// <inheritdoc />
    public async ValueTask<WindowLayout> CreateAsync(
        string name,
        IReadOnlyList<WindowLayoutSlotDefinition> slots,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var (normalizedName, nameKey) = NormalizeName(name);
        ValidateSlots(slots);
        var id = Guid.NewGuid().ToString();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO window_layouts (id, name, name_key, created_at, updated_at)
                    VALUES (@id, @name, @nameKey, @now, @now)
                    """;
                insert.Parameters.AddWithValue("@id", id);
                insert.Parameters.AddWithValue("@name", normalizedName);
                insert.Parameters.AddWithValue("@nameKey", nameKey);
                insert.Parameters.AddWithValue("@now", now.ToString("o"));
                await insert.ExecuteNonQueryAsync(ct);
            }

            await InsertSlotsAsync(connection, transaction, id, slots, ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new WindowLayoutNameConflictException(normalizedName, exception);
        }

        return (await GetByIdAsync(id, ct))!;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RenameAsync(
        string id,
        string name,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var (normalizedName, nameKey) = NormalizeName(name);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE window_layouts
                SET name = @name, name_key = @nameKey, updated_at = @now
                WHERE id = @id
                """;
            command.Parameters.AddWithValue("@name", normalizedName);
            command.Parameters.AddWithValue("@nameKey", nameKey);
            command.Parameters.AddWithValue("@now", now.ToString("o"));
            command.Parameters.AddWithValue("@id", id);
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new WindowLayoutNameConflictException(normalizedName, exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string id,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await DeleteSlotsAsync(connection, transaction, id, ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM window_layouts WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        var deleted = await command.ExecuteNonQueryAsync(ct) > 0;
        await transaction.CommitAsync(ct);
        return deleted;
    }

    private static async ValueTask<IReadOnlyList<WindowLayout>> ReadLayoutsAsync(
        SqliteConnection connection,
        SqliteCommand command,
        CancellationToken ct)
    {
        var rows =
            new List<(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    ParseTimestamp(reader.GetString(2)),
                    ParseTimestamp(reader.GetString(3))));
            }
        }

        if (rows.Count == 0)
            return [];

        var slotsByLayout = await LoadSlotsAsync(
            connection,
            rows.Select(row => row.Id).ToArray(),
            ct);
        return
        [
            .. rows.Select(row => new WindowLayout(
                row.Id,
                row.Name,
                row.CreatedAt,
                row.UpdatedAt,
                slotsByLayout.TryGetValue(row.Id, out var slots) ? slots : [])),
        ];
    }

    private static async ValueTask<Dictionary<string, List<WindowLayoutSlot>>> LoadSlotsAsync(
        SqliteConnection connection,
        IReadOnlyList<string> layoutIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, List<WindowLayoutSlot>>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        var parameters = new string[layoutIds.Count];
        for (var i = 0; i < layoutIds.Count; i++)
        {
            parameters[i] = $"@layout{i}";
            command.Parameters.AddWithValue(parameters[i], layoutIds[i]);
        }

        command.CommandText =
            $"""
            SELECT
                id, layout_id, slot_order, collection_id, captured_window_title,
                monitor_device_name, monitor_is_primary,
                captured_work_x, captured_work_y, captured_work_width, captured_work_height,
                captured_x, captured_y, captured_width, captured_height,
                normalized_x, normalized_y, normalized_width, normalized_height,
                window_state, z_order, desktop_policy
            FROM window_layout_slots
            WHERE layout_id IN ({string.Join(", ", parameters)})
            ORDER BY layout_id, slot_order, id
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var layoutId = reader.GetString(1);
            if (!result.TryGetValue(layoutId, out var slots))
            {
                slots = [];
                result[layoutId] = slots;
            }

            slots.Add(new WindowLayoutSlot(
                reader.GetString(0),
                layoutId,
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6) != 0,
                new WindowRectangle(
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10)),
                new WindowRectangle(
                    reader.GetInt32(11),
                    reader.GetInt32(12),
                    reader.GetInt32(13),
                    reader.GetInt32(14)),
                new NormalizedWindowRectangle(
                    reader.GetDouble(15),
                    reader.GetDouble(16),
                    reader.GetDouble(17),
                    reader.GetDouble(18)),
                ParseState(reader.GetString(19)),
                reader.GetInt32(20),
                ParseDesktopPolicy(reader.GetString(21))));
        }

        return result;
    }

    private static async ValueTask InsertSlotsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string layoutId,
        IReadOnlyList<WindowLayoutSlotDefinition> slots,
        CancellationToken ct)
    {
        foreach (var slot in slots)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO window_layout_slots (
                    id, layout_id, slot_order, collection_id, captured_window_title,
                    monitor_device_name, monitor_is_primary,
                    captured_work_x, captured_work_y, captured_work_width, captured_work_height,
                    captured_x, captured_y, captured_width, captured_height,
                    normalized_x, normalized_y, normalized_width, normalized_height,
                    window_state, z_order, desktop_policy)
                VALUES (
                    @id, @layoutId, @slotOrder, @collectionId, @title,
                    @monitor, @primary,
                    @workX, @workY, @workWidth, @workHeight,
                    @x, @y, @width, @height,
                    @normalizedX, @normalizedY, @normalizedWidth, @normalizedHeight,
                    @state, @zOrder, @desktopPolicy)
                """;
            command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@layoutId", layoutId);
            command.Parameters.AddWithValue("@slotOrder", slot.SlotOrder);
            command.Parameters.AddWithValue("@collectionId", slot.CollectionId);
            command.Parameters.AddWithValue(
                "@title",
                (object?)slot.CapturedWindowTitle ?? DBNull.Value);
            command.Parameters.AddWithValue("@monitor", slot.MonitorDeviceName);
            command.Parameters.AddWithValue("@primary", slot.MonitorIsPrimary ? 1 : 0);
            AddRectangle(command, "work", slot.CapturedWorkArea);
            AddRectangle(command, "", slot.CapturedBounds);
            command.Parameters.AddWithValue("@normalizedX", slot.NormalizedBounds.X);
            command.Parameters.AddWithValue("@normalizedY", slot.NormalizedBounds.Y);
            command.Parameters.AddWithValue("@normalizedWidth", slot.NormalizedBounds.Width);
            command.Parameters.AddWithValue("@normalizedHeight", slot.NormalizedBounds.Height);
            command.Parameters.AddWithValue("@state", FormatState(slot.WindowState));
            command.Parameters.AddWithValue("@zOrder", slot.ZOrder);
            command.Parameters.AddWithValue(
                "@desktopPolicy",
                FormatDesktopPolicy(slot.DesktopPolicy));
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static void AddRectangle(
        SqliteCommand command,
        string prefix,
        WindowRectangle rectangle)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            command.Parameters.AddWithValue("@x", rectangle.X);
            command.Parameters.AddWithValue("@y", rectangle.Y);
            command.Parameters.AddWithValue("@width", rectangle.Width);
            command.Parameters.AddWithValue("@height", rectangle.Height);
            return;
        }

        command.Parameters.AddWithValue($"@{prefix}X", rectangle.X);
        command.Parameters.AddWithValue($"@{prefix}Y", rectangle.Y);
        command.Parameters.AddWithValue($"@{prefix}Width", rectangle.Width);
        command.Parameters.AddWithValue($"@{prefix}Height", rectangle.Height);
    }

    private static async ValueTask DeleteSlotsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string layoutId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM window_layout_slots WHERE layout_id = @layoutId";
        command.Parameters.AddWithValue("@layoutId", layoutId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void ValidateSlots(IReadOnlyList<WindowLayoutSlotDefinition> slots)
    {
        if (slots.Count == 0)
            throw new ArgumentException("A layout requires at least one window.", nameof(slots));
        if (slots.Select(slot => slot.CollectionId).Distinct(StringComparer.Ordinal).Count() !=
            slots.Count)
        {
            throw new ArgumentException(
                "A Collection can appear only once in a layout.",
                nameof(slots));
        }
        if (slots.Select(slot => slot.SlotOrder).Distinct().Count() != slots.Count ||
            slots.Any(slot => slot.SlotOrder < 0))
        {
            throw new ArgumentException(
                "Layout window order values must be unique and non-negative.",
                nameof(slots));
        }

        foreach (var slot in slots)
        {
            if (string.IsNullOrWhiteSpace(slot.CollectionId) ||
                string.IsNullOrWhiteSpace(slot.MonitorDeviceName) ||
                slot.CapturedWorkArea.Width <= 0 ||
                slot.CapturedWorkArea.Height <= 0 ||
                slot.CapturedBounds.Width <= 0 ||
                slot.CapturedBounds.Height <= 0 ||
                !IsFinite(slot.NormalizedBounds.X) ||
                !IsFinite(slot.NormalizedBounds.Y) ||
                !IsFinite(slot.NormalizedBounds.Width) ||
                !IsFinite(slot.NormalizedBounds.Height) ||
                slot.NormalizedBounds.Width <= 0 ||
                slot.NormalizedBounds.Height <= 0)
            {
                throw new ArgumentException("A layout window has invalid placement data.", nameof(slots));
            }
        }
    }

    private static (string Name, string Key) NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A layout name is required.", nameof(name));
        var normalized = name.Trim();
        return (
            normalized,
            normalized.Normalize(NormalizationForm.FormKC).ToUpperInvariant());
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static string FormatState(WindowLayoutState state) =>
        state.ToString().ToLowerInvariant();

    private static WindowLayoutState ParseState(string value) =>
        Enum.TryParse<WindowLayoutState>(value, ignoreCase: true, out var state)
            ? state
            : WindowLayoutState.Normal;

    private static string FormatDesktopPolicy(WindowLayoutDesktopPolicy policy) =>
        policy.ToString().ToLowerInvariant();

    private static WindowLayoutDesktopPolicy ParseDesktopPolicy(string value) =>
        Enum.TryParse<WindowLayoutDesktopPolicy>(value, ignoreCase: true, out var policy)
            ? policy
            : WindowLayoutDesktopPolicy.Current;

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
}
