using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;
using NexusLabs.Narnia.Web.Components;

var builder = WebApplication.CreateBuilder(args);

var options = new NarniaOptions();
builder.Configuration.GetSection(NarniaOptions.SectionName).Bind(options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IFileSystem, FileSystem>();
builder.Services.AddSingleton<SqliteSessionRepository>();
builder.Services.AddSingleton<ISessionSearch>(sp => sp.GetRequiredService<SqliteSessionRepository>());
builder.Services.AddSingleton<SqliteSessionOverridesRepository>();
builder.Services.AddSingleton<ISessionOverridesRepository>(sp => sp.GetRequiredService<SqliteSessionOverridesRepository>());
builder.Services.AddSingleton<OverridingSessionRepository>();
builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<OverridingSessionRepository>());
builder.Services.AddSingleton<NarniaSettingsDbMigrator>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<IWorkspaceReader, WorkspaceReader>();

builder.Services.AddRazorComponents();

var app = builder.Build();

app.Services.GetRequiredService<NarniaSettingsDbMigrator>().MigrateUp();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>();

app.MapPost("/api/sessions/{id}/overrides", async (
    string id,
    OverrideRequest request,
    ISessionOverridesRepository repo,
    CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow;
    var existing = await repo.GetOverrideAsync(id, ct);
    var ov = new SessionOverride(
        id,
        string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
        string.IsNullOrWhiteSpace(request.Repository) ? null : request.Repository.Trim(),
        string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim(),
        string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        existing?.CreatedAt ?? now,
        now);
    await repo.UpsertOverrideAsync(ov, ct);
    return Results.Ok(ov);
});

app.MapDelete("/api/sessions/{id}/overrides", async (
    string id,
    ISessionOverridesRepository repo,
    CancellationToken ct) =>
{
    await repo.DeleteOverrideAsync(id, ct);
    return Results.NoContent();
});

app.MapPost("/api/sessions/{id}/archive", async (
    string id,
    ArchiveRequest request,
    ISessionOverridesRepository repo,
    CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow;
    var existing = await repo.GetOverrideAsync(id, ct);
    var ov = new SessionOverride(
        id,
        existing?.DisplayName,
        existing?.Repository,
        existing?.Branch,
        existing?.Notes,
        existing?.CreatedAt ?? now,
        now)
    {
        IsArchived = request.Archived,
    };
    await repo.UpsertOverrideAsync(ov, ct);
    return Results.Ok();
});

app.Run();

internal sealed record OverrideRequest(
    string? DisplayName,
    string? Repository,
    string? Branch,
    string? Notes);

internal sealed record ArchiveRequest(bool Archived);
