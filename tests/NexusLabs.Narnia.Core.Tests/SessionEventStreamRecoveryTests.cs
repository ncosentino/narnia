using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionEventStreamRecoveryTests
{
    private const string SessionId = "11111111-1111-4111-8111-111111111111";
    private const string MigrationId = "22222222-2222-4222-8222-222222222222";
    private const string Root = @"C:\copilot\session-state";
    private static string SessionDirectory => $@"{Root}\{SessionId}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArchiveAndRestoreAsync_PreservesOriginalAndFailedReplacement()
    {
        const string original = "broken-history";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDirectory}\events.jsonl"] = new(original),
        });
        var recovery = new SessionEventStreamRecovery(
            new NarniaOptions { SessionStatePath = Root },
            fileSystem);

        var plan = await recovery.PlanAsync(SessionId, MigrationId, Ct);
        var archive = await recovery.ArchiveAsync(
            SessionId,
            plan.ArchivePath!,
            plan.Sha256!,
            Ct);

        Assert.True(plan.Planned);
        Assert.True(archive.Archived);
        Assert.NotNull(archive.ArchivePath);
        Assert.False(fileSystem.File.Exists($@"{SessionDirectory}\events.jsonl"));
        Assert.Equal(original, fileSystem.File.ReadAllText(archive.ArchivePath));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))),
            archive.Sha256);

        fileSystem.File.WriteAllText($@"{SessionDirectory}\events.jsonl", "failed-reseed");
        var restore = await recovery.RestoreAsync(
            SessionId,
            MigrationId,
            archive.ArchivePath,
            archive.Sha256!,
            Ct);

        Assert.True(restore.Restored);
        Assert.Equal(
            original,
            fileSystem.File.ReadAllText($@"{SessionDirectory}\events.jsonl"));
        Assert.NotNull(restore.FailedRecoveryPath);
        Assert.Equal(
            "failed-reseed",
            fileSystem.File.ReadAllText(restore.FailedRecoveryPath));
    }

    [Fact]
    public async Task RestoreAsync_HashMismatch_DoesNotMoveFiles()
    {
        var archivePath =
            $@"{SessionDirectory}\events.pre-recovery.{MigrationId}.jsonl";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [archivePath] = new("archived"),
            [$@"{SessionDirectory}\events.jsonl"] = new("replacement"),
        });
        var recovery = new SessionEventStreamRecovery(
            new NarniaOptions { SessionStatePath = Root },
            fileSystem);

        var result = await recovery.RestoreAsync(
            SessionId,
            MigrationId,
            archivePath,
            "WRONG",
            Ct);

        Assert.False(result.Restored);
        Assert.Equal(
            "replacement",
            fileSystem.File.ReadAllText($@"{SessionDirectory}\events.jsonl"));
        Assert.Equal("archived", fileSystem.File.ReadAllText(archivePath));
    }

    [Fact]
    public async Task RestoreAsync_PlannedArchiveNotMoved_TreatsOriginalAsRestored()
    {
        const string original = "broken-history";
        var archivePath =
            $@"{SessionDirectory}\events.pre-recovery.{MigrationId}.jsonl";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDirectory}\events.jsonl"] = new(original),
        });
        var recovery = new SessionEventStreamRecovery(
            new NarniaOptions { SessionStatePath = Root },
            fileSystem);

        var result = await recovery.RestoreAsync(
            SessionId,
            MigrationId,
            archivePath,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))),
            Ct);

        Assert.True(result.Restored);
        Assert.Equal(
            original,
            fileSystem.File.ReadAllText($@"{SessionDirectory}\events.jsonl"));
    }
}
