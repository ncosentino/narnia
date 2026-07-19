using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

/// <summary>
/// Boots the Narnia web app in-memory for endpoint integration tests, pointed at a throwaway
/// settings database and with the background snapshotter disabled. The session repository,
/// terminal command builder, and logon autostart manager are replaced with mocks so tests are
/// deterministic and never touch the real session store, spawn a terminal, or write the registry.
/// </summary>
public sealed class NarniaWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _settingsDbPath = Path.Combine(
        Path.GetTempPath(), $"narnia_web_test_{Guid.NewGuid():N}.db");

    /// <summary>Mock session repository used for tab metadata enrichment.</summary>
    public Mock<ISessionRepository> SessionRepository { get; } = new();

    /// <summary>Mock command builder; by default reports no Windows Terminal so reopen never spawns.</summary>
    public Mock<ITerminalCommandBuilder> CommandBuilder { get; } = new();

    /// <summary>Mock process launcher so endpoint tests never spawn a real terminal.</summary>
    public Mock<IProcessLauncher> ProcessLauncher { get; } = new();

    /// <summary>Mock autostart manager so endpoint tests never touch the real registry.</summary>
    public Mock<ILogonAutostartManager> Autostart { get; } = new();

    /// <summary>Mock scheduled-task provider so tests never shell out to the OS scheduler.</summary>
    public Mock<IScheduledTaskProvider> ScheduledTaskProvider { get; } = new();

    /// <summary>Mock scheduled-task registrar so tests never write the OS scheduler.</summary>
    public Mock<IScheduledTaskRegistrar> ScheduledTaskRegistrar { get; } = new();

    /// <summary>Mock job workspace so tests never write generated scripts to the real filesystem.</summary>
    public Mock<IScheduledJobWorkspace> ScheduledJobWorkspace { get; } = new();

    /// <summary>Mock active-session reader so tests never inspect real Copilot processes or locks.</summary>
    public Mock<ICopilotSessionActivityReader> SessionActivityReader { get; } = new();

    /// <summary>Mock Git safety inspector so cleanup tests never execute Git against real paths.</summary>
    public Mock<IGitArtifactInspector> GitArtifactInspector { get; } = new();

    /// <summary>Mock Copilot SDK boundary so tests never launch a real Copilot runtime.</summary>
    public Mock<ICopilotSessionManager> CopilotSessionManager { get; } = new();

    /// <summary>Mock scan coordinator so endpoint tests never queue a real filesystem scan.</summary>
    public Mock<ISessionStorageScanCoordinator> StorageScanCoordinator { get; } = new();

    /// <summary>Mock cleanup orchestrator for HTTP contract tests.</summary>
    public Mock<ISessionCleanupService> SessionCleanupService { get; } = new();

    public NarniaWebAppFactory()
    {
        // Defaults live here (not in ConfigureTestServices, which runs at host-build time and would
        // otherwise clobber a test's own setup): a supported scheduler that reports no tasks.
        ScheduledTaskProvider.SetupGet(p => p.IsSupported).Returns(true);
        ScheduledTaskProvider
            .Setup(p => p.ListInFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledTaskStatus>)[]);
        ScheduledTaskProvider
            .Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledTaskStatus?)null);

        ScheduledTaskRegistrar.SetupGet(r => r.IsSupported).Returns(true);
        ScheduledTaskRegistrar
            .Setup(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        ScheduledTaskRegistrar
            .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        ScheduledTaskRegistrar
            .Setup(r => r.SetEnabledAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        ScheduledTaskRegistrar
            .Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);

        ScheduledJobWorkspace.Setup(w => w.ScriptPath(It.IsAny<string>()))
            .Returns((string id) => $@"C:\narnia\schedules\{id}\run.ps1");
        ScheduledJobWorkspace.Setup(w => w.LogDirectory(It.IsAny<string>()))
            .Returns((string id) => $@"C:\narnia\schedules\{id}\logs");
        ScheduledJobWorkspace.Setup(w => w.WriteScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string _, CancellationToken _) => $@"C:\narnia\schedules\{id}\run.ps1");

        SessionActivityReader
            .Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        GitArtifactInspector
            .Setup(inspector => inspector.InspectAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitArtifactInspection(true, []));
        CopilotSessionManager
            .Setup(manager => manager.DeleteSessionsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<string> ids, CancellationToken _) =>
                ids.Select(id => new CopilotSessionDeletionResult(id, true, null)).ToArray());
        StorageScanCoordinator
            .Setup(coordinator => coordinator.GetProgress())
            .Returns(new SessionStorageScanProgress("idle", null, null, 0, 0, null));
        StorageScanCoordinator
            .Setup(coordinator => coordinator.RequestScan())
            .Returns(true);
        SessionCleanupService
            .Setup(service => service.PreviewAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionCleanupPreview([], 0, 0, 0, 0, 0));
        SessionCleanupService
            .Setup(service => service.DeleteAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionCleanupBatchResult([]));
    }

    /// <summary>The real (temp-database-backed) terminal windows repository, for seeding.</summary>
    public ITerminalWindowsRepository WindowsRepository =>
        Services.GetRequiredService<ITerminalWindowsRepository>();

    /// <summary>The real (temp-database-backed) session groups repository, for seeding.</summary>
    public ISessionGroupsRepository GroupsRepository =>
        Services.GetRequiredService<ISessionGroupsRepository>();

    /// <summary>The real (temp-database-backed) work collections repository, for seeding.</summary>
    public IWorkCollectionsRepository WorkCollectionsRepository =>
        Services.GetRequiredService<IWorkCollectionsRepository>();

    /// <summary>The real (temp-database-backed) scheduled job registry, for seeding.</summary>
    public IScheduledJobRegistry ScheduledJobRegistry =>
        Services.GetRequiredService<IScheduledJobRegistry>();

    /// <summary>The real temp-database-backed session storage repository.</summary>
    public ISessionStorageRepository StorageRepository =>
        Services.GetRequiredService<ISessionStorageRepository>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Narnia:SettingsDatabasePath", _settingsDbPath);
        builder.UseSetting("Narnia:SnapshotterEnabled", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionRepository>();
            services.AddSingleton(SessionRepository.Object);

            services.RemoveAll<ITerminalCommandBuilder>();
            services.AddSingleton(CommandBuilder.Object);

            services.RemoveAll<IProcessLauncher>();
            services.AddSingleton(ProcessLauncher.Object);

            services.RemoveAll<ILogonAutostartManager>();
            services.AddSingleton(Autostart.Object);

            services.RemoveAll<IScheduledTaskProvider>();
            services.AddSingleton(ScheduledTaskProvider.Object);

            services.RemoveAll<IScheduledTaskRegistrar>();
            services.AddSingleton(ScheduledTaskRegistrar.Object);

            services.RemoveAll<IScheduledJobWorkspace>();
            services.AddSingleton(ScheduledJobWorkspace.Object);

            services.RemoveAll<ICopilotSessionActivityReader>();
            services.AddSingleton(SessionActivityReader.Object);

            services.RemoveAll<IGitArtifactInspector>();
            services.AddSingleton(GitArtifactInspector.Object);

            services.RemoveAll<ICopilotSessionManager>();
            services.AddSingleton(CopilotSessionManager.Object);

            services.RemoveAll<ISessionStorageScanCoordinator>();
            services.AddSingleton(StorageScanCoordinator.Object);

            services.RemoveAll<ISessionCleanupService>();
            services.AddSingleton(SessionCleanupService.Object);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        try
        {
            if (File.Exists(_settingsDbPath))
                File.Delete(_settingsDbPath);
        }
        catch (IOException)
        {
        }
    }
}
