using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteTerminalWindowsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteTerminalWindowsRepository _repository;
    private readonly DateTimeOffset _base = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteTerminalWindowsRepositoryTests()
    {
        var dbName = $"narnia_windows_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        ApplyMigration("0005_add_terminal_windows.sql");

        _repository = new SqliteTerminalWindowsRepository(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task UpsertOpenAsync_NewWindow_IsReturnedAsOpenWithTabs()
    {
        var tabs = new[]
        {
            new TerminalWindowTab("s1", 0, @"C:\dev\one"),
            new TerminalWindowTab("s2", 1, @"C:\dev\two"),
        };

        await _repository.UpsertOpenAsync(100, Key("s1", "s2"), tabs, _base, Ct);

        var open = await _repository.GetOpenAsync(Ct);
        var window = Assert.Single(open);
        Assert.Equal(TerminalWindowStatus.Open, window.Status);
        Assert.Equal(100, window.TerminalProcessId);
        Assert.Equal(2, window.Tabs.Count);
        Assert.Equal("s1", window.Tabs[0].SessionId);
        Assert.Equal(@"C:\dev\two", window.Tabs[1].Directory);
    }

    [Fact]
    public async Task UpsertOpenAsync_SameComposition_DifferentPid_UpdatesInPlace()
    {
        await _repository.UpsertOpenAsync(100, Key("s1"), [new TerminalWindowTab("s1", 0, null)], _base, Ct);
        // The same session is re-detected later under a different terminal process id (e.g. it was
        // relaunched). It must update the existing open record in place, not create a duplicate.
        await _repository.UpsertOpenAsync(
            200,
            Key("s1"),
            [new TerminalWindowTab("s1", 0, @"C:\dev\one")],
            _base.AddSeconds(60),
            Ct);

        var window = Assert.Single(await _repository.GetOpenAsync(Ct));
        Assert.Equal(Key("s1"), window.CompositionKey);
        Assert.Equal(200, window.TerminalProcessId);
        Assert.Equal(@"C:\dev\one", Assert.Single(window.Tabs).Directory);
    }

    [Fact]
    public async Task CloseAsync_MovesWindowFromOpenToClosed()
    {
        await _repository.UpsertOpenAsync(100, Key("s1"), [new TerminalWindowTab("s1", 0, null)], _base, Ct);
        var openId = (await _repository.GetOpenAsync(Ct)).Single().Id;

        await _repository.CloseAsync(openId, _base.AddSeconds(60), Ct);

        Assert.Empty(await _repository.GetOpenAsync(Ct));
        var closed = Assert.Single(await _repository.GetClosedAsync(10, Ct));
        Assert.Equal(TerminalWindowStatus.Closed, closed.Status);
        Assert.Equal(1, closed.OccurrenceCount);
        Assert.Null(closed.TerminalProcessId);
        Assert.Equal("s1", Assert.Single(closed.Tabs).SessionId);
    }

    [Fact]
    public async Task CloseAsync_SameCompositionReopenedAndClosed_DedupesAndCountsOccurrences()
    {
        await _repository.UpsertOpenAsync(100, Key("s1", "s2"), Tabs("s1", "s2"), _base, Ct);
        await _repository.CloseAsync((await _repository.GetOpenAsync(Ct)).Single().Id, _base.AddSeconds(10), Ct);

        await _repository.UpsertOpenAsync(200, Key("s1", "s2"), Tabs("s1", "s2"), _base.AddSeconds(20), Ct);
        await _repository.CloseAsync((await _repository.GetOpenAsync(Ct)).Single().Id, _base.AddSeconds(30), Ct);

        var closed = await _repository.GetClosedAsync(10, Ct);
        var window = Assert.Single(closed);
        Assert.Equal(2, window.OccurrenceCount);
        Assert.Equal(2, window.Tabs.Count);
    }

    [Fact]
    public async Task PruneClosedAsync_KeepsNewestAndNeverPrunesPinned()
    {
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var session = $"session-{i}";
            await _repository.UpsertOpenAsync(1000 + i, Key(session), [new TerminalWindowTab(session, 0, null)], _base.AddSeconds(i), Ct);
            var id = (await _repository.GetOpenAsync(Ct)).Single().Id;
            await _repository.CloseAsync(id, _base.AddSeconds(100 + i), Ct);
            ids.Add(id);
        }

        await _repository.SetNameAsync(ids[0], "keep-me", pinned: true, Ct);

        await _repository.PruneClosedAsync(keepCount: 2, Ct);

        var remaining = (await _repository.GetClosedAsync(100, Ct)).Select(w => w.Id).ToHashSet();
        Assert.Contains(ids[0], remaining); // pinned
        Assert.Contains(ids[4], remaining); // newest
        Assert.Contains(ids[3], remaining); // second newest
        Assert.DoesNotContain(ids[1], remaining);
        Assert.DoesNotContain(ids[2], remaining);
    }

    [Fact]
    public async Task DeleteAsync_RemovesWindowAndTabs()
    {
        await _repository.UpsertOpenAsync(100, Key("s1"), [new TerminalWindowTab("s1", 0, null)], _base, Ct);
        var id = (await _repository.GetOpenAsync(Ct)).Single().Id;

        await _repository.DeleteAsync(id, Ct);

        Assert.Null(await _repository.GetByIdAsync(id, Ct));
        Assert.Empty(await _repository.GetOpenAsync(Ct));
        Assert.Equal(0, await CountTabsAsync(id));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsWindowWithTabs()
    {
        await _repository.UpsertOpenAsync(100, Key("s1", "s2"), Tabs("s1", "s2"), _base, Ct);
        var id = (await _repository.GetOpenAsync(Ct)).Single().Id;

        var window = await _repository.GetByIdAsync(id, Ct);

        Assert.NotNull(window);
        Assert.Equal(id, window!.Id);
        Assert.Equal(2, window.Tabs.Count);
    }

    private static TerminalWindowTab[] Tabs(params string[] sessionIds) =>
        sessionIds.Select((s, i) => new TerminalWindowTab(s, i, null)).ToArray();

    private static string Key(params string[] sessionIds) => TerminalWindowComposition.Key(sessionIds);

    private async Task<long> CountTabsAsync(string windowId)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM terminal_window_tabs WHERE window_id = @id";
        cmd.Parameters.AddWithValue("@id", windowId);
        return (long)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private void ApplyMigration(string fileName)
    {
        var assembly = typeof(SqliteTerminalWindowsRepository).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = reader.ReadToEnd();
        cmd.ExecuteNonQuery();
    }
}
