using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class PathExecutableLocatorTests
{
    [Fact]
    public void Find_ExecutableInFirstPathDirectory_ReturnsFullPath()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\tools\pwsh.exe"] = new MockFileData(""),
        });

        var result = PathExecutableLocator.Find("pwsh.exe", @"C:\tools;C:\other", fs);

        Assert.Equal(@"C:\tools\pwsh.exe", result);
    }

    [Fact]
    public void Find_ExecutableInLaterPathDirectory_ReturnsFullPath()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\other\pwsh.exe"] = new MockFileData(""),
        });

        var result = PathExecutableLocator.Find("pwsh.exe", @"C:\tools;C:\other", fs);

        Assert.Equal(@"C:\other\pwsh.exe", result);
    }

    [Fact]
    public void Find_NotOnAnyPathDirectory_ReturnsNull()
    {
        var fs = new MockFileSystem();

        var result = PathExecutableLocator.Find("pwsh.exe", @"C:\tools;C:\other", fs);

        Assert.Null(result);
    }

    [Fact]
    public void Find_EmptyPathValue_ReturnsNull()
    {
        var fs = new MockFileSystem();

        Assert.Null(PathExecutableLocator.Find("pwsh.exe", "", fs));
        Assert.Null(PathExecutableLocator.Find("pwsh.exe", null, fs));
    }
}
