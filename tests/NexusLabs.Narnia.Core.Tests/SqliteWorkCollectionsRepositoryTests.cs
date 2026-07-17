using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteWorkCollectionsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteWorkCollectionsRepository _repository;
    private readonly DateTimeOffset _base = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteWorkCollectionsRepositoryTests()
    {
        var databaseName = $"narnia_collections_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        ApplyMigration("0010_add_work_collections.sql");

        _repository = new SqliteWorkCollectionsRepository(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task CreateAsync_EmptyCollection_TrimsName()
    {
        var created = await _repository.CreateAsync("  BrandGhost  ", [], _base, Ct);

        Assert.Equal("BrandGhost", created.Name);
        Assert.Empty(created.Members);
        Assert.Equal(_base, created.CreatedAt);
        Assert.Equal(_base, created.UpdatedAt);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameIgnoringCase_ThrowsConflict()
    {
        await _repository.CreateAsync("MCP Tools", [], _base, Ct);

        var exception = await Assert.ThrowsAsync<WorkCollectionNameConflictException>(
            async () => await _repository.CreateAsync(
                "mcp tools",
                [],
                _base.AddMinutes(1),
                Ct));

        Assert.Equal("mcp tools", exception.Name);
    }

    [Fact]
    public async Task CreateAsync_UnicodeEquivalentName_ThrowsConflict()
    {
        await _repository.CreateAsync("Ångström Café", [], _base, Ct);

        await Assert.ThrowsAsync<WorkCollectionNameConflictException>(
            async () => await _repository.CreateAsync(
                "ångström Cafe\u0301",
                [],
                _base.AddMinutes(1),
                Ct));
    }

    [Fact]
    public async Task CreateAsync_DuplicateAndBlankSessionIds_AreCollapsed()
    {
        var created = await _repository.CreateAsync(
            "BrandGhost",
            ["session-1", "session-1", " ", "session-2"],
            _base,
            Ct);

        Assert.Equal(
            ["session-1", "session-2"],
            created.Members.Select(member => member.SessionId).Order());
        Assert.All(created.Members, member => Assert.Equal(_base, member.AddedAt));
    }

    [Fact]
    public async Task GetAllAsync_OrdersCollectionsAlphabetically()
    {
        await _repository.CreateAsync("Zulu", [], _base, Ct);
        await _repository.CreateAsync("alpha", [], _base.AddMinutes(1), Ct);

        var collections = await _repository.GetAllAsync(Ct);

        Assert.Equal(["alpha", "Zulu"], collections.Select(collection => collection.Name));
    }

    [Fact]
    public async Task RenameAsync_UnknownCollection_ReturnsFalse()
    {
        var renamed = await _repository.RenameAsync(
            Guid.NewGuid().ToString(),
            "Missing",
            _base,
            Ct);

        Assert.False(renamed);
    }

    [Fact]
    public async Task RenameAsync_DuplicateNameIgnoringCase_ThrowsConflict()
    {
        var first = await _repository.CreateAsync("BrandGhost", [], _base, Ct);
        await _repository.CreateAsync("MCP Tools", [], _base, Ct);

        await Assert.ThrowsAsync<WorkCollectionNameConflictException>(
            async () => await _repository.RenameAsync(
                first.Id,
                "mcp tools",
                _base.AddMinutes(1),
                Ct));
    }

    [Fact]
    public async Task AddSessionsAsync_IsIdempotentAndAllowsOverlappingCollections()
    {
        var brandGhost = await _repository.CreateAsync(
            "BrandGhost",
            ["session-1"],
            _base,
            Ct);
        await _repository.CreateAsync(
            "MCP Tools",
            ["session-1"],
            _base,
            Ct);

        var added = await _repository.AddSessionsAsync(
            brandGhost.Id,
            ["session-1", "session-2", "session-2"],
            _base.AddMinutes(2),
            Ct);
        var collections = await _repository.GetBySessionIdAsync("session-1", Ct);
        var updated = await _repository.GetByIdAsync(brandGhost.Id, Ct);

        Assert.Equal(1, added);
        Assert.Equal(2, collections.Count);
        Assert.Equal(
            ["session-1", "session-2"],
            updated!.Members.Select(member => member.SessionId).Order());
        Assert.Equal(_base.AddMinutes(2), updated.UpdatedAt);
    }

    [Fact]
    public async Task RemoveSessionsAsync_RemovesOnlyRequestedMemberships()
    {
        var collection = await _repository.CreateAsync(
            "BrandGhost",
            ["session-1", "session-2"],
            _base,
            Ct);

        var removed = await _repository.RemoveSessionsAsync(
            collection.Id,
            ["session-2", "missing"],
            _base.AddMinutes(3),
            Ct);
        var updated = await _repository.GetByIdAsync(collection.Id, Ct);

        Assert.Equal(1, removed);
        Assert.Equal("session-1", Assert.Single(updated!.Members).SessionId);
        Assert.Equal(_base.AddMinutes(3), updated.UpdatedAt);
    }

    [Fact]
    public async Task MembershipChanges_UnknownCollection_ReturnNull()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Null(await _repository.AddSessionsAsync(id, ["session-1"], _base, Ct));
        Assert.Null(await _repository.RemoveSessionsAsync(id, ["session-1"], _base, Ct));
    }

    [Fact]
    public async Task DeleteAsync_RemovesCollectionAndMemberships()
    {
        var collection = await _repository.CreateAsync(
            "Doomed",
            ["session-1"],
            _base,
            Ct);

        var deleted = await _repository.DeleteAsync(collection.Id, Ct);

        Assert.True(deleted);
        Assert.Null(await _repository.GetByIdAsync(collection.Id, Ct));
        Assert.Equal(0, await CountMembersAsync(collection.Id));
    }

    private async Task<long> CountMembersAsync(string collectionId)
    {
        await using var command = _keepAlive.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM work_collection_sessions WHERE collection_id = @id";
        command.Parameters.AddWithValue("@id", collectionId);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private void ApplyMigration(string fileName)
    {
        var assembly = typeof(SqliteWorkCollectionsRepository).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        using var command = _keepAlive.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }
}
