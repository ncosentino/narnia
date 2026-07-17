using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SessionsPageTests
{
    [Fact]
    public async Task CreatedDateAndWorkingDirectoryPrefix_FilterUnderlyingSessions()
    {
        using var factory = new NarniaWebAppFactory();
        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.Date);
        var prefix = @"C:\Temp\bg-eval-judge";
        var target = Summary(
            "target",
            prefix + @"\10873c94704f4fcea064cda3049c6251",
            "Matching evaluation session",
            now);
        factory.SessionRepository
            .Setup(repository => repository.ListByActivitySourceAsync(
                It.Is<SessionActivitySourceFilter>(filter =>
                    filter.Date == date
                    && filter.Kind == SessionActivitySourceKind.WorkingDirectory
                    && filter.WorkingDirectory == prefix
                    && filter.IncludesGeneratedChildren
                    && filter.HostTypeMissing),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([target]);
        factory.SessionRepository
            .Setup(repository => repository.GetResumableSessionIdsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var client = factory.CreateClient();
        var url =
            $"/sessions?createdDate={date:yyyy-MM-dd}" +
            $"&activitySourceKind=WorkingDirectory" +
            $"&activitySource={Uri.EscapeDataString(prefix)}" +
            "&activityGeneratedChildren=true" +
            "&activityHostTypeMissing=true" +
            "&showArchived=true";
        var html = await client.GetStringAsync(url, TestContext.Current.CancellationToken);

        Assert.Contains("Matching evaluation session", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Previous-day evaluation session", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Interactive session", html, StringComparison.Ordinal);
        Assert.Contains($"Sessions on {date:yyyy-MM-dd}", html, StringComparison.Ordinal);
        Assert.Contains("Recorded source:", html, StringComparison.Ordinal);
    }

    private static SessionSummary Summary(
        string id,
        string cwd,
        string summary,
        DateTimeOffset createdAt) =>
        new(
            id,
            cwd,
            null,
            null,
            summary,
            createdAt,
            createdAt,
            1,
            0);
}
