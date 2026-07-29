using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ProcessDiagnosticsServiceTests
{
    private static readonly DateTimeOffset Started =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetSnapshotAsync_MapsRuntimeToTerminalAndSharedSessions()
    {
        const string primaryId = "11111111-1111-4111-8111-111111111111";
        const string nestedId = "22222222-2222-4222-8222-222222222222";
        var provider = Provider(Snapshot(
            Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 0),
            Process(20, 10, "pwsh.exe", 1, 5, 8, 0),
            Process(30, 20, "node.exe", 2, 15, 20, 0),
            Process(40, 30, "copilot.exe", 3, 100, 120, 0),
            Process(50, 40, "analytics-mcp.exe", 4, 25, 30, 0)));
        var locks = new Mock<ICopilotSessionLockReader>();
        locks.Setup(reader => reader.GetSessionIdsByProcess(
                It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 40 }))))
            .Returns(new Dictionary<int, IReadOnlyList<string>>
            {
                [40] = [nestedId, primaryId],
            });
        var sessions = new Mock<ISessionRepository>();
        sessions.Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase)
            {
                [primaryId] = Session(primaryId, "Primary", Started),
                [nestedId] = Session(nestedId, "Nested", Started.AddMinutes(1)),
            });
        var service = CreateService(provider, locks, sessions);

        var snapshot = await service.GetSnapshotAsync(Ct);

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(2, snapshot.MappedSessionCount);
        var terminal = Assert.Single(snapshot.Terminals);
        Assert.Equal(10, terminal.TerminalProcessId);
        Assert.Equal(3, terminal.OtherUsage.ProcessCount);
        var runtime = Assert.Single(terminal.Runtimes);
        Assert.Equal(40, runtime.CopilotProcessId);
        Assert.Equal(20, runtime.ShellProcessId);
        Assert.Equal(10, runtime.TerminalProcessId);
        Assert.Equal([20, 30], runtime.LaunchChain.Select(process => process.ProcessId));
        Assert.Equal(2, runtime.RuntimeTree.TreeUsage.ProcessCount);
        Assert.Equal(primaryId, runtime.Sessions.Single(session => session.IsPrimary).SessionId);
        Assert.Equal("Nested", runtime.Sessions.Single(session => !session.IsPrimary).Summary);
        Assert.Null(snapshot.CopilotRuntimeUsage.CpuPercent);
    }

    [Fact]
    public async Task GetSnapshotAsync_SecondSampleComputesNormalizedDeduplicatedCpu()
    {
        const string sessionId = "11111111-1111-4111-8111-111111111111";
        var first = Snapshot(
            Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 1),
            Process(40, 10, "copilot.exe", 1, 100, 120, 2),
            Process(50, 40, "child.exe", 2, 25, 30, 1));
        var second = SnapshotAt(
            TimeSpan.FromSeconds(12),
            Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 1),
            Process(40, 10, "copilot.exe", 1, 100, 120, 6),
            Process(50, 40, "child.exe", 2, 25, 30, 3));
        var provider = new Mock<IProcessResourceSnapshotProvider>();
        provider.SetupGet(item => item.IsSupported).Returns(true);
        provider.SetupSequence(item => item.Capture(It.IsAny<CancellationToken>()))
            .Returns(first)
            .Returns(second);
        var locks = Locks(40, sessionId);
        var sessions = Sessions(Session(sessionId, "Session", Started));
        var time = new ManualTimeProvider();
        var service = new ProcessDiagnosticsService(
            provider.Object,
            locks.Object,
            sessions.Object,
            time);

        await service.GetSnapshotAsync(Ct);
        time.Advance(TimeSpan.FromSeconds(2));
        var snapshot = await service.GetSnapshotAsync(Ct);

        Assert.Equal(2d, snapshot.SampleDurationSeconds);
        Assert.NotNull(snapshot.CopilotRuntimeUsage.CpuPercent);
        Assert.Equal(75d, snapshot.CopilotRuntimeUsage.CpuPercent.Value, precision: 6);
        Assert.Equal(2, snapshot.CopilotRuntimeUsage.ProcessCount);
        Assert.Equal(2, snapshot.CopilotRuntimeUsage.CpuSampledProcessCount);
        Assert.NotNull(snapshot.TerminalUsage.CpuPercent);
        Assert.Equal(75d, snapshot.TerminalUsage.CpuPercent.Value, precision: 6);
        locks.Verify(reader => reader.GetSessionIdsByProcess(
            It.IsAny<IReadOnlyCollection<int>>()), Times.Once);
        sessions.Verify(repository => repository.GetByIdsAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSnapshotAsync_ChildCopilotWithoutSessionLockIsNotAnotherRuntime()
    {
        const string sessionId = "11111111-1111-4111-8111-111111111111";
        var provider = Provider(Snapshot(
            Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 0),
            Process(40, 10, "copilot.exe", 1, 100, 120, 0),
            Process(41, 40, "copilot.exe", 2, 50, 60, 0)));
        var service = CreateService(
            provider,
            Locks(40, sessionId),
            Sessions(Session(sessionId, "Session", Started)));

        var snapshot = await service.GetSnapshotAsync(Ct);

        var terminal = Assert.Single(snapshot.Terminals);
        var runtime = Assert.Single(terminal.Runtimes);
        Assert.Equal(40, runtime.CopilotProcessId);
        Assert.Equal(41, Assert.Single(runtime.RuntimeTree.Children).ProcessId);
        Assert.Empty(snapshot.OrphanedRuntimes);
    }

    [Fact]
    public async Task GetSnapshotAsync_ChildChangeKeepsOwnershipSignatureButChangesTreeSignature()
    {
        const string sessionId = "11111111-1111-4111-8111-111111111111";
        var provider = new Mock<IProcessResourceSnapshotProvider>();
        provider.SetupGet(item => item.IsSupported).Returns(true);
        provider.SetupSequence(item => item.Capture(It.IsAny<CancellationToken>()))
            .Returns(Snapshot(
                Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 1),
                Process(40, 10, "copilot.exe", 1, 100, 120, 2)))
            .Returns(SnapshotAt(
                TimeSpan.FromSeconds(12),
                Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 1),
                Process(40, 10, "copilot.exe", 1, 100, 120, 2),
                Process(50, 40, "child.exe", 2, 25, 30, 1)));
        var time = new ManualTimeProvider();
        var service = new ProcessDiagnosticsService(
            provider.Object,
            Locks(40, sessionId).Object,
            Sessions(Session(sessionId, "Session", Started)).Object,
            time);

        var first = await service.GetSnapshotAsync(Ct);
        time.Advance(TimeSpan.FromSeconds(2));
        var second = await service.GetSnapshotAsync(Ct);

        Assert.Equal(first.TopologySignature, second.TopologySignature);
        Assert.NotEqual(first.ProcessTreeSignature, second.ProcessTreeSignature);
        Assert.NotEqual(
            string.Join("|", first.ProcessTreeIdentities),
            string.Join("|", second.ProcessTreeIdentities));
    }

    [Fact]
    public async Task GetSnapshotAsync_SessionMetadataChangeUpdatesOwnershipSignature()
    {
        const string sessionId = "11111111-1111-4111-8111-111111111111";
        var provider = new Mock<IProcessResourceSnapshotProvider>();
        provider.SetupGet(item => item.IsSupported).Returns(true);
        provider.SetupSequence(item => item.Capture(It.IsAny<CancellationToken>()))
            .Returns(Snapshot(
                Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 1),
                Process(40, 10, "copilot.exe", 1, 100, 120, 2)))
            .Returns(SnapshotAt(
                TimeSpan.FromSeconds(45),
                Process(10, 0, "WindowsTerminal.exe", 0, 10, 20, 1),
                Process(40, 10, "copilot.exe", 1, 100, 120, 2)));
        var sessions = new Mock<ISessionRepository>();
        sessions.SetupSequence(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Session>
            {
                [sessionId] = Session(sessionId, "Before", Started),
            })
            .ReturnsAsync(new Dictionary<string, Session>
            {
                [sessionId] = Session(sessionId, "After", Started),
            });
        var time = new ManualTimeProvider();
        var service = new ProcessDiagnosticsService(
            provider.Object,
            Locks(40, sessionId).Object,
            sessions.Object,
            time);

        var first = await service.GetSnapshotAsync(Ct);
        time.Advance(TimeSpan.FromSeconds(35));
        var second = await service.GetSnapshotAsync(Ct);

        Assert.NotEqual(first.TopologySignature, second.TopologySignature);
        Assert.Equal(
            "After",
            Assert.Single(Assert.Single(second.Terminals).Runtimes).Sessions.Single().Summary);
    }

    [Fact]
    public async Task GetSnapshotAsync_RejectsParentThatStartedAfterChild()
    {
        var provider = Provider(Snapshot(
            Process(10, 0, "WindowsTerminal.exe", 10, 10, 20, 0),
            Process(40, 10, "copilot.exe", 1, 100, 120, 0)));
        var service = CreateService(provider, Locks(), Sessions());

        var snapshot = await service.GetSnapshotAsync(Ct);

        Assert.Empty(Assert.Single(snapshot.Terminals).Runtimes);
        Assert.Equal(40, Assert.Single(snapshot.OrphanedRuntimes).CopilotProcessId);
    }

    [Fact]
    public async Task GetSnapshotAsync_CyclicParentLinksAreRejected()
    {
        var provider = Provider(Snapshot(
            Process(40, 50, "copilot.exe", 1, 100, 120, 0),
            Process(50, 40, "node.exe", 1, 25, 30, 0)));
        var service = CreateService(provider, Locks(), Sessions());

        var snapshot = await service.GetSnapshotAsync(Ct);

        var runtime = Assert.Single(snapshot.OrphanedRuntimes);
        Assert.Equal(1, runtime.RuntimeTree.TreeUsage.ProcessCount);
        Assert.Empty(runtime.RuntimeTree.Children);
    }

    [Fact]
    public async Task GetSnapshotAsync_UnsupportedProviderReturnsExplicitUnavailableSnapshot()
    {
        var provider = new Mock<IProcessResourceSnapshotProvider>();
        provider.SetupGet(item => item.IsSupported).Returns(false);
        provider.Setup(item => item.Capture(It.IsAny<CancellationToken>()))
            .Returns(new ProcessResourceSnapshot(
                false,
                "Not supported.",
                Started,
                TimeSpan.Zero,
                4,
                []));
        var locks = new Mock<ICopilotSessionLockReader>(MockBehavior.Strict);
        var sessions = new Mock<ISessionRepository>(MockBehavior.Strict);
        var service = CreateService(provider, locks, sessions);

        var snapshot = await service.GetSnapshotAsync(Ct);

        Assert.False(snapshot.IsAvailable);
        Assert.Equal("Not supported.", snapshot.UnavailableReason);
        Assert.Empty(snapshot.Terminals);
    }

    private static ProcessDiagnosticsService CreateService(
        Mock<IProcessResourceSnapshotProvider> provider,
        Mock<ICopilotSessionLockReader> locks,
        Mock<ISessionRepository> sessions) =>
        new(provider.Object, locks.Object, sessions.Object, new ManualTimeProvider());

    private static Mock<IProcessResourceSnapshotProvider> Provider(
        ProcessResourceSnapshot snapshot)
    {
        var provider = new Mock<IProcessResourceSnapshotProvider>();
        provider.SetupGet(item => item.IsSupported).Returns(true);
        provider.Setup(item => item.Capture(It.IsAny<CancellationToken>()))
            .Returns(snapshot);
        return provider;
    }

    private static Mock<ICopilotSessionLockReader> Locks(
        int? processId = null,
        params string[] sessionIds)
    {
        var locks = new Mock<ICopilotSessionLockReader>();
        locks.Setup(reader => reader.GetSessionIdsByProcess(
                It.IsAny<IReadOnlyCollection<int>>()))
            .Returns(processId is null
                ? new Dictionary<int, IReadOnlyList<string>>()
                : new Dictionary<int, IReadOnlyList<string>>
                {
                    [processId.Value] = sessionIds,
                });
        return locks;
    }

    private static Mock<ISessionRepository> Sessions(params Session[] sessions)
    {
        var byId = sessions.ToDictionary(session => session.Id, StringComparer.OrdinalIgnoreCase);
        var repository = new Mock<ISessionRepository>();
        repository.Setup(item => item.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(byId);
        return repository;
    }

    private static Session Session(
        string id,
        string summary,
        DateTimeOffset createdAt) =>
        new(id, @"C:\dev\example", "owner/repo", "main", summary, null, createdAt, createdAt);

    private static ProcessResourceSnapshot Snapshot(
        params ProcessResourceRecord[] processes) =>
        SnapshotAt(TimeSpan.FromSeconds(10), processes);

    private static ProcessResourceSnapshot SnapshotAt(
        TimeSpan monotonicTime,
        params ProcessResourceRecord[] processes) =>
        new(
            true,
            null,
            Started.Add(monotonicTime),
            monotonicTime,
            4,
            processes);

    private static ProcessResourceRecord Process(
        int processId,
        int parentProcessId,
        string name,
        int startedAfterSeconds,
        long privateMegabytes,
        long workingSetMegabytes,
        double processorSeconds) =>
        new(
            processId,
            parentProcessId,
            name,
            Started.AddSeconds(startedAfterSeconds),
            TimeSpan.FromSeconds(processorSeconds),
            privateMegabytes * 1024 * 1024,
            workingSetMegabytes * 1024 * 1024);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp = 10_000;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) =>
            _timestamp += (long)(duration.TotalSeconds * TimestampFrequency);
    }
}
