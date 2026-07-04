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
