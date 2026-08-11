namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Documentation is only useful to a reader or an agent that can reach it. These tests keep every
/// maintained page discoverable and every cross-reference resolvable.
/// </summary>
public sealed class DocumentationMapTests
{
    /// <summary>
    /// Pages deliberately excluded from navigation, with the reason each one is kept.
    /// </summary>
    private static readonly Dictionary<string, string> NavigationExclusions = new(StringComparer.Ordinal)
    {
        ["docs/tools/open-narnia-ui.md"] =
            "Deprecation tombstone kept so the published URL keeps resolving after the tool was removed.",
    };

    [Fact]
    public void EveryDocumentationPage_IsReachableFromNavigation()
    {
        var navigation = GuidanceContract
            .NavigationTargets(RepositoryLayout.ReadText("mkdocs.yml"))
            .Select(target => $"docs/{target}")
            .ToHashSet(StringComparer.Ordinal);

        var unreachable = RepositoryLayout
            .DocumentationFiles()
            .Where(page => !navigation.Contains(page))
            .Where(page => !NavigationExclusions.ContainsKey(page))
            .ToList();

        Assert.True(
            unreachable.Count == 0,
            "These documentation pages are not reachable from the mkdocs.yml navigation: " +
            string.Join(", ", unreachable) +
            ". Add them to the navigation or record an explicit exclusion with its reason.");
    }

    [Fact]
    public void NavigationExclusions_StillExist()
    {
        foreach (var (page, reason) in NavigationExclusions)
        {
            Assert.True(
                RepositoryLayout.Exists(page),
                $"'{page}' is recorded as an intentional navigation exclusion ({reason}) but no longer exists. " +
                "Remove the exclusion.");
        }
    }

    [Fact]
    public void EveryNavigationEntry_PointsAtAPageThatExists()
    {
        var missing = GuidanceContract
            .NavigationTargets(RepositoryLayout.ReadText("mkdocs.yml"))
            .Select(target => $"docs/{target}")
            .Distinct(StringComparer.Ordinal)
            .Where(page => !RepositoryLayout.Exists(page))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "mkdocs.yml navigates to pages that do not exist: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryLocalDocumentationLink_Resolves()
    {
        var sources = RepositoryLayout
            .DocumentationFiles()
            .Concat(RepositoryLayout.InstructionFiles())
            .Concat([".github/skills/review-changes/SKILL.md", "AGENTS.md"])
            .Where(RepositoryLayout.Exists)
            .ToList();

        var failures = new List<string>();
        foreach (var source in sources)
        {
            var content = RepositoryLayout.ReadText(source);
            var sourceDirectory = Path.GetDirectoryName(source)?.Replace('\\', '/') ?? string.Empty;

            foreach (var target in GuidanceContract.LocalLinkTargets(content))
            {
                var fragmentIndex = target.IndexOf('#');
                var filePart = fragmentIndex < 0 ? target : target[..fragmentIndex];
                var fragment = fragmentIndex < 0 ? null : target[(fragmentIndex + 1)..];
                if (filePart.Length == 0)
                    continue;

                var combined = sourceDirectory.Length == 0
                    ? filePart
                    : $"{sourceDirectory}/{filePart}";
                var resolved = NormalizePath(combined);

                if (!RepositoryLayout.Exists(resolved))
                {
                    failures.Add($"{source} -> {target} (resolved to '{resolved}')");
                    continue;
                }

                if (fragment is null ||
                    !resolved.EndsWith(".md", StringComparison.Ordinal))
                {
                    continue;
                }

                var anchors = GuidanceContract.HeadingAnchors(RepositoryLayout.ReadText(resolved));
                if (!anchors.Contains(fragment, StringComparer.Ordinal))
                    failures.Add($"{source} -> {target} (no heading in '{resolved}' produces anchor '{fragment}')");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Unresolvable documentation links:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static string NormalizePath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    continue;
                default:
                    segments.Add(segment);
                    continue;
            }
        }

        return string.Join('/', segments);
    }
}
