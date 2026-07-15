using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledJobWorkspaceTests
{
    private const string SchedulesDir = @"C:\narnia\schedules";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NarniaOptions Options() => new() { SchedulesDirectory = SchedulesDir };

    [Fact]
    public void ScriptPath_ReturnsRunPs1UnderJobFolder()
    {
        var workspace = new ScheduledJobWorkspace(Options(), new MockFileSystem());

        Assert.Equal(@"C:\narnia\schedules\job-1\run.ps1", workspace.ScriptPath("job-1"));
    }

    [Fact]
    public void LauncherPath_ReturnsRunVbsUnderJobFolder()
    {
        var workspace = new ScheduledJobWorkspace(Options(), new MockFileSystem());

        Assert.Equal(@"C:\narnia\schedules\job-1\run.vbs", workspace.LauncherPath("job-1"));
    }

    [Fact]
    public void LogDirectory_ReturnsLogsUnderJobFolder()
    {
        var workspace = new ScheduledJobWorkspace(Options(), new MockFileSystem());

        Assert.Equal(@"C:\narnia\schedules\job-1\logs", workspace.LogDirectory("job-1"));
    }

    [Fact]
    public async Task WriteScriptAsync_CreatesFolderAndWritesFile()
    {
        var fs = new MockFileSystem();
        var workspace = new ScheduledJobWorkspace(Options(), fs);

        var path = await workspace.WriteScriptAsync("job-1", "# script content", Ct);

        Assert.Equal(@"C:\narnia\schedules\job-1\run.ps1", path);
        Assert.Equal("# script content", fs.File.ReadAllText(path));
    }

    [Fact]
    public async Task WriteLauncherAsync_CreatesFolderAndWritesFile()
    {
        var fs = new MockFileSystem();
        var workspace = new ScheduledJobWorkspace(Options(), fs);

        var path = await workspace.WriteLauncherAsync("job-1", "' vbs content", Ct);

        Assert.Equal(@"C:\narnia\schedules\job-1\run.vbs", path);
        Assert.Equal("' vbs content", fs.File.ReadAllText(path));
        var bytes = fs.File.ReadAllBytes(path);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
    }

    [Fact]
    public void LatestLogFile_NoLogDirectory_ReturnsNull()
    {
        var workspace = new ScheduledJobWorkspace(Options(), new MockFileSystem());

        Assert.Null(workspace.LatestLogFile("job-1"));
    }

    [Fact]
    public void LatestLogFile_EmptyLogDirectory_ReturnsNull()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(@"C:\narnia\schedules\job-1\logs");
        var workspace = new ScheduledJobWorkspace(Options(), fs);

        Assert.Null(workspace.LatestLogFile("job-1"));
    }

    [Fact]
    public void LatestLogFile_MultipleRuns_ReturnsMostRecentByName()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\narnia\schedules\job-1\logs\run-2026-07-01_050000.log"] = new MockFileData("old"),
            [@"C:\narnia\schedules\job-1\logs\run-2026-07-04_020000.log"] = new MockFileData("newest"),
            [@"C:\narnia\schedules\job-1\logs\run-2026-07-02_050000.log"] = new MockFileData("middle"),
        });
        var workspace = new ScheduledJobWorkspace(Options(), fs);

        var latest = workspace.LatestLogFile("job-1");

        Assert.Equal(@"C:\narnia\schedules\job-1\logs\run-2026-07-04_020000.log", latest);
    }

    [Fact]
    public async Task ReadLogAsync_ReturnsFileContent()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\narnia\schedules\job-1\logs\run-2026-07-04_020000.log"] = new MockFileData("log body"),
        });
        var workspace = new ScheduledJobWorkspace(Options(), fs);

        var content = await workspace.ReadLogAsync(@"C:\narnia\schedules\job-1\logs\run-2026-07-04_020000.log", Ct);

        Assert.Equal("log body", content);
    }

    [Fact]
    public async Task ReadLogAsync_FileHasConcurrentWriterNotSharingWriteAccess_DoesNotThrow()
    {
        // Regression test for a real bug found live: the scheduled task's own wrapper keeps its log
        // handle open across the whole run (PowerShell's Tee-Object -Append), so reading with the
        // .NET default share mode threw IOException while a job was still executing -- the exact
        // moment the live-polling log viewer needs to read it. Verified empirically (both against
        // the real running job and a faithful Tee-Object repro) that the failure requires the
        // reader's own FileShare to include Write, not just Read -- a plain FileShare.Read reader
        // still throws against a writer-open handle. Uses the real file system (not MockFileSystem,
        // which has no OS-level locking model) against a real temp file with a second handle
        // deliberately left open, matching Tee-Object's behavior.
        var tempPath = Path.Combine(Path.GetTempPath(), $"narnia-log-test-{Guid.NewGuid():N}.log");
        var realFs = new System.IO.Abstractions.FileSystem();
        try
        {
            await using var writer = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var textWriter = new StreamWriter(writer) { AutoFlush = false };
            await textWriter.WriteAsync("partial output so far");
            await textWriter.FlushAsync(Ct);

            // Proves this test would have caught the original bug: a reader that does not request
            // FileShare.Write (the .NET default for File.ReadAllTextAsync) is rejected by the OS
            // while the writer handle above is still open.
            await Assert.ThrowsAsync<IOException>(() => realFs.File.ReadAllTextAsync(tempPath, Ct));

            var workspace = new ScheduledJobWorkspace(Options(), realFs);
            var content = await workspace.ReadLogAsync(tempPath, Ct);

            Assert.Equal("partial output so far", content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Delete_RemovesEntireJobFolder()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\narnia\schedules\job-1\run.ps1"] = new MockFileData("s"),
            [@"C:\narnia\schedules\job-1\run.vbs"] = new MockFileData("v"),
            [@"C:\narnia\schedules\job-1\logs\run-2026-07-04_020000.log"] = new MockFileData("l"),
        });
        var workspace = new ScheduledJobWorkspace(Options(), fs);

        workspace.Delete("job-1");

        Assert.False(fs.Directory.Exists(@"C:\narnia\schedules\job-1"));
    }

    [Fact]
    public void Delete_MissingFolder_DoesNotThrow()
    {
        var workspace = new ScheduledJobWorkspace(Options(), new MockFileSystem());

        workspace.Delete("never-existed");
    }
}
