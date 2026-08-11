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
        ".venv",
        ".wrangler",
        "artifacts",
        "bin",
        "coverage",
        "dist",
        "node_modules",
        "obj",
        "site",
        "venv",
    ];

    private static readonly Lazy<string> LazyRoot = new(Locate);
    private static readonly Lazy<IReadOnlyList<string>> LazyFiles = new(Enumerate);

    public static string Root => LazyRoot.Value;

    public static string Path(params string[] segments) =>
        System.IO.Path.Combine([Root, .. segments]);

    public static bool Exists(string relativePath) =>
        File.Exists(System.IO.Path.Combine(Root, Normalize(relativePath)));

    public static string ReadText(string relativePath) =>
        File.ReadAllText(System.IO.Path.Combine(Root, Normalize(relativePath)));

    /// <summary>
    /// Repository-relative, forward-slashed paths of every file Git considers part of the
    /// working tree. Ignored files are excluded, so a contributor's local scratch notes cannot
    /// fail a structural test. Falls back to a filtered directory walk when Git is unavailable.
    /// </summary>
    public static IReadOnlyList<string> AllFiles() => LazyFiles.Value;

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

    private static IReadOnlyList<string> Enumerate()
    {
        var results = FromGit() ?? FromDirectoryWalk();
        var ordered = results.ToList();
        ordered.Sort(StringComparer.Ordinal);
        return ordered;
    }

    private static IReadOnlyList<string>? FromGit()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                ArgumentList = { "-C", Root, "ls-files", "--cached", "--others", "--exclude-standard" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return null;

            var files = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Replace('\\', '/'))
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return files.Count == 0 ? null : files;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> FromDirectoryWalk()
    {
        var results = new List<string>();
        Collect(new DirectoryInfo(Root), results);
        return results;
    }

    private static void Collect(DirectoryInfo directory, List<string> results)
    {
        try
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
        catch (UnauthorizedAccessException)
        {
            // A directory the test process cannot read contributes nothing to the contract.
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
