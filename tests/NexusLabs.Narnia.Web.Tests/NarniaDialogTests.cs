namespace NexusLabs.Narnia.Web.Tests;

public sealed class NarniaDialogTests
{
    [Fact]
    public async Task Page_RendersSharedAccessibleDialogHostAndScript()
    {
        using var factory = new NarniaWebAppFactory();
        var html = await factory.CreateClient().GetStringAsync(
            "/",
            TestContext.Current.CancellationToken);

        Assert.Contains("<dialog id=\"narnia-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "aria-labelledby=\"narnia-dialog-title\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "src=\"js/narnia-dialog.js?v=",
            html,
            StringComparison.Ordinal);
    }
}
