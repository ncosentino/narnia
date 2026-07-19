using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class CopilotSessionLockResolverTests
{
    private const string SessionStatePath = @"C:\copilot\session-state";

    private static NarniaOptions CreateOptions() => new()
    {
        SessionStatePath = SessionStatePath,
    };

    [Fact]
    public void ResolveSessionId_NoSessionStateDirectory_ReturnsNull()
    {
        var fs = new MockFileSystem();
        var resolver = CreateResolver(fs);

        Assert.Null(resolver.ResolveSessionId(1234));
    }

    [Fact]
    public void ResolveSessionId_NoMatchingLockFile_ReturnsNull()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionStatePath}\some-session\inuse.999.lock"] = new MockFileData("999"),
        });
        var resolver = CreateResolver(fs);

        Assert.Null(resolver.ResolveSessionId(1234));
    }

    [Fact]
    public void ResolveSessionId_OneMatchingLockFile_ReturnsContainingSessionId()
    {
        const string sessionId = "a5f24071-030e-4342-91c8-af73be9dc266";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionStatePath}\{sessionId}\inuse.64768.lock"] = new MockFileData("64768"),
        });
        var resolver = CreateResolver(fs);

        Assert.Equal(sessionId, resolver.ResolveSessionId(64768));
    }

    [Fact]
    public void ResolveSessionId_MultipleMatchingLockFiles_PrefersOldestSessionFolder()
    {
        // A single agent process can hold locks in more than one session-state folder when it
        // has spawned an in-process sub-agent/background task. The oldest folder is the
        // top-level session the user is looking at; the newer one is a nested sub-task.
        const string mainSessionId = "08a86a76-3fa9-4f49-aa52-a4af41ac0d98";
        const string subTaskSessionId = "40f39afa-e80c-4817-b522-c59ec3e0d7ae";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionStatePath}\{mainSessionId}\inuse.67424.lock"] = new MockFileData("67424"),
            [$@"{SessionStatePath}\{subTaskSessionId}\inuse.67424.lock"] = new MockFileData("67424"),
        });
        fs.Directory.SetCreationTimeUtc($@"{SessionStatePath}\{mainSessionId}", new DateTime(2026, 7, 2, 3, 47, 1, DateTimeKind.Utc));
        fs.Directory.SetCreationTimeUtc($@"{SessionStatePath}\{subTaskSessionId}", new DateTime(2026, 7, 3, 6, 47, 12, DateTimeKind.Utc));
        var resolver = CreateResolver(fs);

        Assert.Equal(mainSessionId, resolver.ResolveSessionId(67424));
    }

    [Fact]
    public void ResolveSessionId_DifferentPidsResolveToDifferentSessions()
    {
        const string sessionA = "11111111-1111-4111-8111-111111111111";
        const string sessionB = "22222222-2222-4222-8222-222222222222";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionStatePath}\{sessionA}\inuse.100.lock"] = new MockFileData("100"),
            [$@"{SessionStatePath}\{sessionB}\inuse.200.lock"] = new MockFileData("200"),
        });
        var resolver = CreateResolver(fs);

        Assert.Equal(sessionA, resolver.ResolveSessionId(100));
        Assert.Equal(sessionB, resolver.ResolveSessionId(200));
    }

    private static CopilotSessionLockResolver CreateResolver(MockFileSystem fileSystem)
    {
        var options = CreateOptions();
        return new CopilotSessionLockResolver(
            options,
            fileSystem,
            new CopilotSessionLockReader(options, fileSystem));
    }
}
