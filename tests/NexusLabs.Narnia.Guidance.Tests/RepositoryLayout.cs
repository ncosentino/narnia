namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Locates the repository root and enumerates the files these tests inspect. The guidance
/// contract is about repository structure, so these tests read the working tree directly rather
/// than any compiled artifact.
/// </summary>
internal static class RepositoryLayout
{
    private static readonly string[] ExcludedDirectories =
    [
        ".git",
        ".wrangler",
        "artifacts",
        "bin",
        "coverage",
        "dist",
        "node_modules",
        "obj",
        "site",
    ];

    private static readonly Lazy<string> LazyRoot = new(Locate);

    public static string Root => LazyRoot.Value;

    public static string Path(params string[] segments) =>
        System.IO.Path.Combine([Root, .. segments]);

    public static bool Exists(string relativePath) =>
        File.Exists(System.IO.Path.Combine(Root, Normalize(relativePath)));

    public static string ReadText(string relativePath) =>
        File.ReadAllText(System.IO.Path.Combine(Root, Normalize(relativePath)));

    /// <summary>
    /// Repository-relative, forward-slashed paths of every file outside build output.
    /// </summary>
    public static IReadOnlyList<string> AllFiles()
    {
        var results = new List<string>();
        Collect(new DirectoryInfo(Root), results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    public static IReadOnlyList<string> DocumentationFiles() =>
        [.. AllFiles()
            .Where(path =>
                path.StartsWith("docs/", StringComparison.Ordinal) &&
                path.EndsWith(".md", StringComparison.Ordinal) &&
                !path.StartsWith("docs/overrides/", StringComparison.Ordinal))];

    public static IReadOnlyList<string> InstructionFiles() =>
        [.. AllFiles()
            .Where(path =>
                path.StartsWith(".github/instructions/", StringComparison.Ordinal) &&
                path.EndsWith(".instructions.md", StringComparison.Ordinal))];

    public static string ToRelative(string absolutePath) =>
        System.IO.Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');

    private static string Normalize(string relativePath) =>
        relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);

    private static void Collect(DirectoryInfo directory, List<string> results)
    {
        foreach (var file in directory.EnumerateFiles())
            results.Add(ToRelative(file.FullName));

        foreach (var child in directory.EnumerateDirectories())
        {
            if (ExcludedDirectories.Contains(child.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;

            Collect(child, results);
        }
    }

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "narnia.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
