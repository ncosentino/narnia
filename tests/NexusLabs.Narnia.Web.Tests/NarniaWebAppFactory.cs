using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>The real (temp-database-backed) terminal windows repository, for seeding.</summary>
    public ITerminalWindowsRepository WindowsRepository =>
        Services.GetRequiredService<ITerminalWindowsRepository>();

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
