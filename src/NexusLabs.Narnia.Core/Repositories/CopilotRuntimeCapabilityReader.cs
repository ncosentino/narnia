using System.IO.Abstractions;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Inspects installed Copilot package changelogs for explicit runtime capabilities.</summary>
public sealed class CopilotRuntimeCapabilityReader(
    NarniaOptions options,
    IFileSystem fileSystem) : ICopilotRuntimeCapabilityReader
{
    private const string LargeSessionEvidence =
        "Resume very large local sessions and continue with new prompts reliably.";

    /// <inheritdoc />
    public CopilotRuntimeCapability Read()
    {
        try
        {
            var copilotRoot = fileSystem.Path.GetDirectoryName(
                fileSystem.Path.GetFullPath(options.SessionStatePath));
            if (string.IsNullOrWhiteSpace(copilotRoot))
                return Unknown(null, "The Copilot package root could not be resolved.");

            var packageRoot = fileSystem.Path.Combine(copilotRoot, "pkg");
            if (!fileSystem.Directory.Exists(packageRoot))
                return Unknown(null, "No installed Copilot package directory was found.");

            var platformDirectory = fileSystem.Path.Combine(packageRoot, "win32-x64");
            if (!fileSystem.Directory.Exists(platformDirectory))
                return Unknown(null, "No installed Windows x64 Copilot package directory was found.");

            var packages = fileSystem.Directory
                .EnumerateDirectories(platformDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(versionDirectory => new
                {
                    Path = fileSystem.Path.Combine(versionDirectory, "changelog.json"),
                    Version = fileSystem.Path.GetFileName(versionDirectory),
                })
                .Where(package =>
                    TryParseVersion(package.Version, out _) &&
                    fileSystem.File.Exists(package.Path))
                .OrderByDescending(package => package.Version, VersionStringComparer.Instance)
                .ToArray();
            if (packages.Length == 0)
                return Unknown(null, "No installed Copilot package changelog was found.");

            var selected = packages[0];
            using var stream = fileSystem.File.Open(
                selected.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Unknown(selected.Version, "The installed Copilot changelog has an unsupported shape.");

            string? capabilityVersion = null;
            foreach (var release in document.RootElement.EnumerateObject())
            {
                if (!TryParseVersion(release.Name, out _) || release.Value.ValueKind != JsonValueKind.Array)
                    continue;
                if (release.Value.EnumerateArray().Any(IsCapabilityEntry) &&
                    (capabilityVersion is null || VersionStringComparer.Instance.Compare(release.Name, capabilityVersion) > 0))
                {
                    capabilityVersion = release.Name;
                }
            }

            if (capabilityVersion is null)
            {
                return new CopilotRuntimeCapability(
                    CopilotLargeSessionResumeSupport.Unsupported,
                    selected.Version,
                    "The installed changelog does not advertise reliable very-large-session resume.");
            }

            var supported = VersionStringComparer.Instance.Compare(
                selected.Version!, capabilityVersion) >= 0;
            return new CopilotRuntimeCapability(
                supported
                    ? CopilotLargeSessionResumeSupport.Supported
                    : CopilotLargeSessionResumeSupport.Unsupported,
                selected.Version,
                supported
                    ? $"Capability advertised since Copilot {capabilityVersion}."
                    : $"Capability is advertised beginning with Copilot {capabilityVersion}.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unknown(null, $"Installed Copilot capability could not be read: {exception.Message}");
        }
    }

    private static bool IsCapabilityEntry(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object &&
        entry.TryGetProperty("description", out var description) &&
        description.ValueKind == JsonValueKind.String &&
        string.Equals(description.GetString(), LargeSessionEvidence, StringComparison.Ordinal);

    private static CopilotRuntimeCapability Unknown(string? version, string evidence) =>
        new(CopilotLargeSessionResumeSupport.Unknown, version, evidence);

    private static bool TryParseVersion(string? value, out int[] components)
    {
        components = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var core = value.Split('-', 2)[0];
        var parts = core.Split('.');
        if (parts.Length < 3 || parts.Any(part => !int.TryParse(part, out _)))
            return false;
        components = parts.Select(int.Parse).ToArray();
        return true;
    }

    private sealed class VersionStringComparer : IComparer<string?>
    {
        public static VersionStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (!TryParseVersion(x, out var left) || !TryParseVersion(y, out var right))
                return StringComparer.Ordinal.Compare(x, y);
            for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                var comparison = (index < left.Length ? left[index] : 0)
                    .CompareTo(index < right.Length ? right[index] : 0);
                if (comparison != 0)
                    return comparison;
            }

            var leftSuffix = x!.Contains('-') ? x[(x.IndexOf('-') + 1)..] : null;
            var rightSuffix = y!.Contains('-') ? y[(y.IndexOf('-') + 1)..] : null;
            if (leftSuffix is null && rightSuffix is not null)
                return 1;
            if (leftSuffix is not null && rightSuffix is null)
                return -1;
            if (int.TryParse(leftSuffix, out var leftNumber) && int.TryParse(rightSuffix, out var rightNumber))
                return leftNumber.CompareTo(rightNumber);
            return StringComparer.Ordinal.Compare(leftSuffix, rightSuffix);
        }
    }
}
