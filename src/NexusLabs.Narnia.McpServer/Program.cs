using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.McpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

var options = new NarniaOptions();
// Allow override via environment variables: NARNIA__DatabasePath, NARNIA__SessionStatePath
var dbPath = Environment.GetEnvironmentVariable("NARNIA__DatabasePath");
var statePath = Environment.GetEnvironmentVariable("NARNIA__SessionStatePath");
if (!string.IsNullOrWhiteSpace(dbPath)) options.DatabasePath = dbPath;
if (!string.IsNullOrWhiteSpace(statePath)) options.SessionStatePath = statePath;
builder.Services.AddSingleton(options);

builder.Services.AddSingleton<IFileSystem, FileSystem>();
builder.Services.AddSingleton<SqliteSessionRepository>();
builder.Services.AddSingleton<ISessionSearch>(sp => sp.GetRequiredService<SqliteSessionRepository>());
builder.Services.AddSingleton<SqliteSessionOverridesRepository>();
builder.Services.AddSingleton<ISessionOverridesRepository>(sp => sp.GetRequiredService<SqliteSessionOverridesRepository>());
builder.Services.AddSingleton<OverridingSessionRepository>();
builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<OverridingSessionRepository>());
builder.Services.AddSingleton<NarniaSettingsDbMigrator>();
builder.Services.AddSingleton<IWorkspaceReader, WorkspaceReader>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SessionTools>();

var app = builder.Build();
app.Services.GetRequiredService<NarniaSettingsDbMigrator>().MigrateUp();
await app.RunAsync();
