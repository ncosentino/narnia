using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.McpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

var options = new NarniaOptions();
// Allow override via environment variables: NARNIA__DatabasePath, NARNIA__SessionStatePath, NARNIA__WebUiUrl, NARNIA__WebProjectPath
var dbPath = Environment.GetEnvironmentVariable("NARNIA__DatabasePath");
var statePath = Environment.GetEnvironmentVariable("NARNIA__SessionStatePath");
var webUiUrl = Environment.GetEnvironmentVariable("NARNIA__WebUiUrl");
var webProjectPath = Environment.GetEnvironmentVariable("NARNIA__WebProjectPath");
if (!string.IsNullOrWhiteSpace(dbPath)) options.DatabasePath = dbPath;
if (!string.IsNullOrWhiteSpace(statePath)) options.SessionStatePath = statePath;
if (!string.IsNullOrWhiteSpace(webUiUrl)) options.WebUiUrl = webUiUrl;
if (!string.IsNullOrWhiteSpace(webProjectPath)) options.WebProjectPath = webProjectPath;
builder.Services.AddSingleton(options);

builder.Services.AddSingleton<IFileSystem, FileSystem>();
builder.Services.AddSingleton<SqliteSessionRepository>();
builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<SqliteSessionRepository>());
builder.Services.AddSingleton<ISessionSearch>(sp => sp.GetRequiredService<SqliteSessionRepository>());
builder.Services.AddSingleton<IWorkspaceReader, WorkspaceReader>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SessionTools>();

var app = builder.Build();
await app.RunAsync();
