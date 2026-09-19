using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class CopilotRuntimeCapabilityReaderTests
{
    private const string SessionStatePath = @"C:\copilot\session-state";

    [Fact]
    public void Read_SelectsNewestPackageAndRecognizesCapability()
    {
        var fileSystem = FileSystemWithChangelog(
            "1.0.87-0",
            """{"1.0.87-0":[{"type":"fixed","description":"Resume very large local sessions and continue with new prompts reliably."}]}""");
        fileSystem.AddFile(
            @"C:\copilot\pkg\win32-x64\1.0.86\changelog.json",
            new MockFileData("{}"));

        var result = CreateReader(fileSystem).Read();

        Assert.Equal(CopilotLargeSessionResumeSupport.Supported, result.Support);
        Assert.Equal("1.0.87-0", result.Version);
    }

    [Fact]
    public void Read_ValidChangelogWithoutCapability_IsUnsupported()
    {
        var fileSystem = FileSystemWithChangelog(
            "1.0.86",
            """{"1.0.86":[{"type":"fixed","description":"Another fix"}]}""");

        var result = CreateReader(fileSystem).Read();

        Assert.Equal(CopilotLargeSessionResumeSupport.Unsupported, result.Support);
        Assert.Equal("1.0.86", result.Version);
    }

    [Fact]
    public void Read_MalformedChangelog_IsUnknown()
    {
        var fileSystem = FileSystemWithChangelog("1.0.87-0", "{invalid");

        var result = CreateReader(fileSystem).Read();

        Assert.Equal(CopilotLargeSessionResumeSupport.Unknown, result.Support);
    }

    [Fact]
    public void Read_MissingPackageDirectory_IsUnknown()
    {
        var result = CreateReader(new MockFileSystem()).Read();

        Assert.Equal(CopilotLargeSessionResumeSupport.Unknown, result.Support);
        Assert.Null(result.Version);
    }

    private static MockFileSystem FileSystemWithChangelog(string version, string changelog) =>
        new(new Dictionary<string, MockFileData>
        {
            [$@"C:\copilot\pkg\win32-x64\{version}\changelog.json"] = new(changelog),
        });

    private static CopilotRuntimeCapabilityReader CreateReader(MockFileSystem fileSystem) =>
        new(
            new NarniaOptions { SessionStatePath = SessionStatePath },
            fileSystem);
}
