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
        _repository = new SqliteWindowLayoutsRepository(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task CreateAsync_RoundTripsPlacementAndOrdersByName()
    {
        await _repository.CreateAsync("Zulu", [Slot("collection-z", 0)], _now, Ct);
        var created = await _repository.CreateAsync(
            "  Alpha  ",
            [Slot("collection-a", 0), Slot("collection-b", 1)],
            _now.AddMinutes(1),
            Ct);

        var layouts = await _repository.GetAllAsync(Ct);

        Assert.Equal(["Alpha", "Zulu"], layouts.Select(layout => layout.Name));
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
        await _repository.CreateAsync("Daily", [Slot("collection-a", 0)], _now, Ct);

        await Assert.ThrowsAsync<WindowLayoutNameConflictException>(
            async () => await _repository.CreateAsync(
                "daily",
                [Slot("collection-b", 0)],
                _now,
                Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _repository.CreateAsync(
                "Other",
                [Slot("collection-a", 0), Slot("collection-a", 1)],
                _now,
                Ct));
    }

    [Fact]
    public async Task RenameAndDelete_PersistChanges()
    {
        var created = await _repository.CreateAsync(
            "Daily",
            [Slot("collection-a", 0)],
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

    private static WindowLayoutSlotDefinition Slot(string collectionId, int order) =>
        new(
            order,
            collectionId,
            collectionId,
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2112),
            new WindowRectangle(order * 1276, 0, 1276, 1056),
            new NormalizedWindowRectangle(order / 3d, 0, 1d / 3d, 0.5),
            WindowLayoutState.Normal,
            order,
            WindowLayoutDesktopPolicy.Current);

    private void ApplyMigration(string fileName)
    {
        var assembly = typeof(SqliteWindowLayoutsRepository).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        using var command = _keepAlive.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }
}
