using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteSessionGroupsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteSessionGroupsRepository _repository;
    private readonly DateTimeOffset _base = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteSessionGroupsRepositoryTests()
    {
        var dbName = $"narnia_groups_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        ApplyMigration("0006_add_session_groups.sql");

        _repository = new SqliteSessionGroupsRepository(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task CreateAsync_NewGroup_IsReturnedWithMembersInOrder()
    {
        var created = await _repository.CreateAsync("Morning set", ["s3", "s1", "s2"], _base, Ct);

        Assert.Equal("Morning set", created.Name);
        Assert.Equal(_base, created.CreatedAt);
        Assert.Equal(_base, created.UpdatedAt);
        Assert.Equal(["s3", "s1", "s2"], created.Members.Select(m => m.SessionId));
        Assert.Equal([0, 1, 2], created.Members.Select(m => m.MemberOrder));

        var fetched = await _repository.GetByIdAsync(created.Id, Ct);
        Assert.NotNull(fetched);
        Assert.Equal(["s3", "s1", "s2"], fetched!.Members.Select(m => m.SessionId));
    }

    [Fact]
    public async Task CreateAsync_DuplicateAndBlankSessionIds_AreCollapsed()
    {
        var created = await _repository.CreateAsync("Dupes", ["s1", "s1", "  ", "s2", "s1"], _base, Ct);

        Assert.Equal(["s1", "s2"], created.Members.Select(m => m.SessionId));
        Assert.Equal([0, 1], created.Members.Select(m => m.MemberOrder));
    }

    [Fact]
    public async Task GetAllAsync_OrdersByUpdatedAtDescending()
    {
        await _repository.CreateAsync("First", ["a"], _base, Ct);
        await _repository.CreateAsync("Second", ["b"], _base.AddMinutes(5), Ct);

        var all = await _repository.GetAllAsync(Ct);

        Assert.Equal(["Second", "First"], all.Select(g => g.Name));
    }

    [Fact]
    public async Task RenameAsync_ChangesNameAndUpdatedAt()
    {
        var created = await _repository.CreateAsync("Old", ["a"], _base, Ct);

        await _repository.RenameAsync(created.Id, "New", _base.AddMinutes(10), Ct);

        var fetched = await _repository.GetByIdAsync(created.Id, Ct);
        Assert.Equal("New", fetched!.Name);
        Assert.Equal(_base.AddMinutes(10), fetched.UpdatedAt);
        Assert.Equal(_base, fetched.CreatedAt);
    }

    [Fact]
    public async Task SetMembersAsync_ReplacesMembershipAndTouchesUpdatedAt()
    {
        var created = await _repository.CreateAsync("Set", ["a", "b"], _base, Ct);

        await _repository.SetMembersAsync(created.Id, ["c", "a", "d"], _base.AddMinutes(3), Ct);

        var fetched = await _repository.GetByIdAsync(created.Id, Ct);
        Assert.Equal(["c", "a", "d"], fetched!.Members.Select(m => m.SessionId));
        Assert.Equal([0, 1, 2], fetched.Members.Select(m => m.MemberOrder));
        Assert.Equal(_base.AddMinutes(3), fetched.UpdatedAt);
    }

    [Fact]
    public async Task DeleteAsync_RemovesGroupAndMembers()
    {
        var created = await _repository.CreateAsync("Doomed", ["a", "b"], _base, Ct);

        await _repository.DeleteAsync(created.Id, Ct);

        Assert.Null(await _repository.GetByIdAsync(created.Id, Ct));
        Assert.Equal(0, await CountMembersAsync(created.Id));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _repository.GetByIdAsync(Guid.NewGuid().ToString(), Ct));
    }

    private async Task<long> CountMembersAsync(string groupId)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM session_group_members WHERE group_id = @id";
        cmd.Parameters.AddWithValue("@id", groupId);
        return (long)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private void ApplyMigration(string fileName)
    {
        var assembly = typeof(SqliteSessionGroupsRepository).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = reader.ReadToEnd();
        cmd.ExecuteNonQuery();
    }
}
