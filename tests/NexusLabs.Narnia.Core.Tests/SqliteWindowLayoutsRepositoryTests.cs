using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteWindowLayoutsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteWindowLayoutsRepository _repository;
    private readonly DateTimeOffset _now =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteWindowLayoutsRepositoryTests()
    {
        var databaseName = $"narnia_layouts_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        ApplyMigration("0016_add_window_layouts.sql");
        ApplyMigration("0017_add_layout_editor_content.sql");
        _repository = new SqliteWindowLayoutsRepository(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task CreateAsync_RoundTripsPlacementAndOrdersByName()
    {
        await _repository.CreateAsync(
            "Zulu",
            Monitors(),
            [CollectionSlot("collection-z", 0)],
            _now,
            Ct);
        var created = await _repository.CreateAsync(
            "  Alpha  ",
            Monitors(),
            [
                CollectionSlot("collection-a", 0),
                SessionSlot("session-b", 1),
            ],
            _now.AddMinutes(1),
            Ct);

        var layouts = await _repository.GetAllAsync(Ct);

        Assert.Equal(["Alpha", "Zulu"], layouts.Select(layout => layout.Name));
        Assert.Single(created.Monitors);
        Assert.Equal(2, created.Slots.Count);
        var first = created.Slots[0];
        Assert.Equal("collection-a", first.CollectionId);
        Assert.Equal(new WindowRectangle(0, 0, 3840, 2112), first.CapturedWorkArea);
        Assert.Equal(new WindowRectangle(0, 0, 1276, 1056), first.CapturedBounds);
        Assert.Equal(new NormalizedWindowRectangle(0, 0, 1d / 3d, 0.5), first.NormalizedBounds);
        Assert.Equal(WindowLayoutState.Normal, first.WindowState);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameOrCollection_IsRejected()
    {
        await _repository.CreateAsync(
            "Daily",
            Monitors(),
            [CollectionSlot("collection-a", 0)],
            _now,
            Ct);

        await Assert.ThrowsAsync<WindowLayoutNameConflictException>(
            async () => await _repository.CreateAsync(
                "daily",
                Monitors(),
                [CollectionSlot("collection-b", 0)],
                _now,
                Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _repository.CreateAsync(
                "Other",
                Monitors(),
                [
                    CollectionSlot("collection-a", 0),
                    CollectionSlot("collection-a", 1),
                ],
                _now,
                Ct));
    }

    [Fact]
    public async Task RenameAndDelete_PersistChanges()
    {
        var created = await _repository.CreateAsync(
            "Daily",
            Monitors(),
            [CollectionSlot("collection-a", 0)],
            _now,
            Ct);

        Assert.True(await _repository.RenameAsync(
            created.Id,
            "Updated",
            _now.AddMinutes(1),
            Ct));
        var updated = await _repository.GetByIdAsync(created.Id, Ct);
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal("collection-a", Assert.Single(updated.Slots).CollectionId);
        Assert.True(await _repository.DeleteAsync(created.Id, Ct));
        Assert.Null(await _repository.GetByIdAsync(created.Id, Ct));
    }

    [Fact]
    public async Task ReplaceDefinition_AllowsEmptyAndMixedContent()
    {
        var created = await _repository.CreateAsync(
            "Editable",
            Monitors(),
            [],
            _now,
            Ct);

        Assert.True(await _repository.ReplaceDefinitionAsync(
            created.Id,
            Monitors(),
            [
                CollectionSlot("collection-a", 0),
                SessionSlot("session-b", 1),
            ],
            _now.AddMinutes(1),
            Ct));

        var updated = await _repository.GetByIdAsync(created.Id, Ct);
        Assert.Equal(2, updated!.Slots.Count);
        Assert.Equal(WindowLayoutContentKind.Collection, updated.Slots[0].ContentKind);
        Assert.Equal(WindowLayoutContentKind.Session, updated.Slots[1].ContentKind);
    }

    [Fact]
    public async Task Migration0017_PreservesCapturedCollectionLayout()
    {
        var databaseName = $"narnia_layout_migration_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(Ct);
        ApplyMigration(connection, "0016_add_window_layouts.sql");
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText =
                """
                INSERT INTO window_layouts (id, name, name_key, created_at, updated_at)
                VALUES ('layout', 'Daily', 'DAILY', @now, @now);
                INSERT INTO window_layout_slots (
                    id, layout_id, slot_order, collection_id, captured_window_title,
                    monitor_device_name, monitor_is_primary,
                    captured_work_x, captured_work_y, captured_work_width, captured_work_height,
                    captured_x, captured_y, captured_width, captured_height,
                    normalized_x, normalized_y, normalized_width, normalized_height,
                    window_state, z_order, desktop_policy)
                VALUES (
                    'slot', 'layout', 0, 'collection-a', 'Foundation',
                    '\\.\DISPLAY1', 1,
                    0, 0, 3840, 2112,
                    0, 0, 1276, 1056,
                    0, 0, 0.3322916667, 0.5,
                    'normal', 0, 'current');
                """;
            seed.Parameters.AddWithValue("@now", _now.ToString("o"));
            await seed.ExecuteNonQueryAsync(Ct);
        }

        ApplyMigration(connection, "0017_add_layout_editor_content.sql");
        var repository = new SqliteWindowLayoutsRepository(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });

        var layout = await repository.GetByIdAsync("layout", Ct);

        Assert.NotNull(layout);
        var monitor = Assert.Single(layout!.Monitors);
        Assert.Equal(
            new WindowRectangle(0, 0, 3840, 2112),
            monitor.CapturedBounds);
        Assert.Equal(monitor.CapturedBounds, monitor.CapturedWorkArea);
        var slot = Assert.Single(layout.Slots);
        Assert.Equal(WindowLayoutContentKind.Collection, slot.ContentKind);
        Assert.Equal("collection-a", slot.CollectionId);
        Assert.Null(slot.SessionId);
    }

    private static IReadOnlyList<WindowLayoutMonitorDefinition> Monitors() =>
    [
        new(
            0,
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2160),
            new WindowRectangle(0, 0, 3840, 2112)),
    ];

    private static WindowLayoutSlotDefinition CollectionSlot(
        string collectionId,
        int order) =>
        new(
            order,
            WindowLayoutContentKind.Collection,
            collectionId,
            null,
            collectionId,
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2112),
            new WindowRectangle(order * 1276, 0, 1276, 1056),
            new NormalizedWindowRectangle(order / 3d, 0, 1d / 3d, 0.5),
            WindowLayoutState.Normal,
            order,
            WindowLayoutDesktopPolicy.Current);

    private static WindowLayoutSlotDefinition SessionSlot(
        string sessionId,
        int order) =>
        new(
            order,
            WindowLayoutContentKind.Session,
            null,
            sessionId,
            sessionId,
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2112),
            new WindowRectangle(order * 1276, 0, 1276, 1056),
            new NormalizedWindowRectangle(order / 3d, 0, 1d / 3d, 0.5),
            WindowLayoutState.Normal,
            order,
            WindowLayoutDesktopPolicy.Current);

    private void ApplyMigration(string fileName) =>
        ApplyMigration(_keepAlive, fileName);

    private static void ApplyMigration(SqliteConnection connection, string fileName)
    {
        var assembly = typeof(SqliteWindowLayoutsRepository).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        using var command = connection.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }
}
