using System.Text.Json;

namespace NexusLabs.Narnia.Guidance.Tests;

public sealed class PluginHookTests
{
    [Fact]
    public void SessionStartHook_HandlesBothPublishedDeploymentKinds()
    {
        using var document = JsonDocument.Parse(RepositoryLayout.ReadText("hooks.json"));
        var hook = document.RootElement
            .GetProperty("hooks")
            .GetProperty("sessionStart")[0];
        var powershell = hook.GetProperty("powershell").GetString();

        Assert.NotNull(powershell);
        Assert.Contains("start-server.ps1", powershell, StringComparison.Ordinal);
        Assert.Contains("runtimeconfig.json", powershell, StringComparison.Ordinal);
        Assert.Contains("$frameworkDependent", powershell, StringComparison.Ordinal);
        Assert.Contains("Get-Command dotnet.exe", powershell, StringComparison.Ordinal);
        Assert.Contains("NexusLabs.Narnia.Web.exe", powershell, StringComparison.Ordinal);
        Assert.Contains("NexusLabs.Narnia.Web.dll", powershell, StringComparison.Ordinal);
    }
}
