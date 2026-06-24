using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SettingsDatabaseRelocatorTests
{
    private static readonly string LegacyPath = NarniaOptions.GetLegacySettingsDatabasePath();
    private const string DestPath = @"C:\Users\tester\AppData\Local\narnia\settings.db";

    private static NarniaOptions Options() => new() { SettingsDatabasePath = DestPath };

    [Fact]
    public void RelocateIfNeeded_LegacyPresentDestAbsent_MovesDatabase()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LegacyPath] = new MockFileData("legacy-db-bytes"),
        });
        var relocator = new SettingsDatabaseRelocator(Options(), fs);

        relocator.RelocateIfNeeded();

        Assert.True(fs.File.Exists(DestPath));
        Assert.Equal("legacy-db-bytes", fs.File.ReadAllText(DestPath));
        Assert.False(fs.File.Exists(LegacyPath));
    }

    [Fact]
    public void RelocateIfNeeded_MovesWalAndShmSidecars()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LegacyPath] = new MockFileData("db"),
            [LegacyPath + "-wal"] = new MockFileData("wal"),
            [LegacyPath + "-shm"] = new MockFileData("shm"),
        });
        var relocator = new SettingsDatabaseRelocator(Options(), fs);

        relocator.RelocateIfNeeded();

        Assert.Equal("wal", fs.File.ReadAllText(DestPath + "-wal"));
        Assert.Equal("shm", fs.File.ReadAllText(DestPath + "-shm"));
        Assert.False(fs.File.Exists(LegacyPath + "-wal"));
        Assert.False(fs.File.Exists(LegacyPath + "-shm"));
    }

    [Fact]
    public void RelocateIfNeeded_DestinationExists_LeavesBothUntouched()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LegacyPath] = new MockFileData("legacy"),
            [DestPath] = new MockFileData("existing-dest"),
        });
        var relocator = new SettingsDatabaseRelocator(Options(), fs);

        relocator.RelocateIfNeeded();

        Assert.Equal("existing-dest", fs.File.ReadAllText(DestPath));
        Assert.True(fs.File.Exists(LegacyPath));
    }

    [Fact]
    public void RelocateIfNeeded_NoLegacy_CreatesDestinationDirectoryOnly()
    {
        var fs = new MockFileSystem();
        var relocator = new SettingsDatabaseRelocator(Options(), fs);

        relocator.RelocateIfNeeded();

        Assert.False(fs.File.Exists(DestPath));
        Assert.True(fs.Directory.Exists(@"C:\Users\tester\AppData\Local\narnia"));
    }

    [Fact]
    public void RelocateIfNeeded_ConnectionStringOverride_IsNoOp()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LegacyPath] = new MockFileData("legacy"),
        });
        var options = new NarniaOptions
        {
            SettingsDatabasePath = DestPath,
            SettingsConnectionString = "Data Source=:memory:",
        };
        var relocator = new SettingsDatabaseRelocator(options, fs);

        relocator.RelocateIfNeeded();

        Assert.False(fs.File.Exists(DestPath));
        Assert.True(fs.File.Exists(LegacyPath));
    }

    [Fact]
    public void RelocateIfNeeded_RunTwice_IsIdempotent()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LegacyPath] = new MockFileData("legacy"),
        });
        var relocator = new SettingsDatabaseRelocator(Options(), fs);

        relocator.RelocateIfNeeded();
        relocator.RelocateIfNeeded();

        Assert.Equal("legacy", fs.File.ReadAllText(DestPath));
        Assert.False(fs.File.Exists(LegacyPath));
    }

    [Fact]
    public void RelocateIfNeeded_CopyThrows_DoesNotPropagate_AndCleansPartialDest()
    {
        // A copy failure (e.g. the legacy database momentarily locked by an old server) must not
        // crash startup; the legacy stays intact and any partial destination is removed.
        var file = new Mock<IFile>(MockBehavior.Strict);
        file.Setup(f => f.Exists(DestPath)).Returns(false);
        file.Setup(f => f.Exists(LegacyPath)).Returns(true);
        file.Setup(f => f.Exists(LegacyPath + "-wal")).Returns(false);
        file.Setup(f => f.Exists(LegacyPath + "-shm")).Returns(false);
        file.Setup(f => f.Copy(LegacyPath, DestPath, false)).Throws(new IOException("locked"));
        file.Setup(f => f.Delete(DestPath));

        var directory = new Mock<IDirectory>(MockBehavior.Loose);
        var path = new Mock<IPath>();
        path.Setup(p => p.GetDirectoryName(DestPath)).Returns(@"C:\Users\tester\AppData\Local\narnia");
        path.Setup(p => p.GetFullPath(It.IsAny<string>())).Returns<string>(s => s);

        var fs = new Mock<IFileSystem>();
        fs.SetupGet(f => f.File).Returns(file.Object);
        fs.SetupGet(f => f.Directory).Returns(directory.Object);
        fs.SetupGet(f => f.Path).Returns(path.Object);

        var relocator = new SettingsDatabaseRelocator(Options(), fs.Object);

        var exception = Record.Exception(() => relocator.RelocateIfNeeded());

        Assert.Null(exception);
        file.Verify(f => f.Delete(DestPath), Times.Once);
        file.Verify(f => f.Delete(LegacyPath), Times.Never);
    }
}
