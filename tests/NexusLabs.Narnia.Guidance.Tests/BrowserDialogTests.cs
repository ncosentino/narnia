using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>Prevents browser-native dialogs from returning to Narnia's web UI.</summary>
public sealed partial class BrowserDialogTests
{
    [Fact]
    public void WebScripts_DoNotCallBrowserNativeDialogs()
    {
        var failures = new List<string>();
        foreach (var file in RepositoryLayout.AllFiles().Where(path =>
            path.StartsWith(
                "src/NexusLabs.Narnia.Web/",
                StringComparison.Ordinal) &&
            (path.EndsWith(".js", StringComparison.Ordinal) ||
             path.EndsWith(".razor", StringComparison.Ordinal))))
        {
            var lines = RepositoryLayout.ReadText(file).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (BrowserDialogCall().IsMatch(lines[index]))
                    failures.Add($"{file}:{index + 1}: {lines[index].Trim()}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Use the shared asynchronous narniaDialog host instead of browser-native " +
            "alert, confirm, or prompt calls:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void DialogGuidance_RequiresTheSharedHost()
    {
        const string path = ".github/instructions/ui-dialogs.instructions.md";
        var guidance = RepositoryLayout.ReadText(path);

        Assert.Contains("Browser-native", guidance, StringComparison.Ordinal);
        Assert.Contains("narniaDialog", guidance, StringComparison.Ordinal);
        Assert.Contains("danger: true", guidance, StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"(?<![\w.])(?:alert|confirm|prompt)\s*\(|" +
        @"(?:window|globalThis|self)\.(?:alert|confirm|prompt)\s*\(")]
    private static partial Regex BrowserDialogCall();
}
