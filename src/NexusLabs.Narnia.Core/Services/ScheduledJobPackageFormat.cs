using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

internal static class ScheduledJobPackageFormat
{
    public const string Format = "narnia.schedule-package";
    public const int SchemaVersion = 1;
    public const int MaxPackageChars = 5_000_000;
    public const int MaxJobs = 100;
    public const int MaxPromptChars = 1_000_000;

    public static string Serialize(ScheduledJobPackage package) =>
        JsonSerializer.Serialize(
            package,
            ScheduledJobPackageJsonContext.Default.ScheduledJobPackage);

    public static ScheduledJobPackageParseResult ParseAndValidate(string packageJson)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
            return new(null, "Package JSON is required.");
        if (packageJson.Length > MaxPackageChars)
            return new(null, $"Package JSON exceeds the {MaxPackageChars:N0}-character limit.");

        ScheduledJobPackage? package;
        try
        {
            package = JsonSerializer.Deserialize(
                packageJson,
                ScheduledJobPackageJsonContext.Default.ScheduledJobPackage);
        }
        catch (JsonException ex)
        {
            return new(null, $"Package JSON is invalid: {ex.Message}");
        }

        if (package is null)
            return new(null, "Package JSON did not contain a package.");
        if (!string.Equals(package.Format, Format, StringComparison.Ordinal))
            return new(null, $"Unsupported package format '{package.Format}'.");
        if (package.SchemaVersion != SchemaVersion)
            return new(null, $"Unsupported package schema version {package.SchemaVersion}.");
        if (!Enum.IsDefined(package.Profile))
            return new(null, "Package profile is invalid.");
        if (string.IsNullOrWhiteSpace(package.PackageId))
            return new(null, "Package id is required.");
        if (package.Source is null ||
            string.IsNullOrWhiteSpace(package.Source.TimeZoneId) ||
            package.Bindings is null ||
            package.Dependencies is null ||
            package.Jobs is null)
        {
            return new(null, "Package source, bindings, dependencies, and jobs are required.");
        }
        if (package.Jobs.Count is < 1 or > MaxJobs)
            return new(null, $"A package must contain between 1 and {MaxJobs} jobs.");
        if (package.Bindings.Any(binding =>
                binding is null ||
                string.IsNullOrWhiteSpace(binding.Id) ||
                !ScheduledJobPackageText.IsValidIdentifier(binding.Id)))
        {
            return new(null, "Package binding ids must contain only lowercase letters, numbers, and hyphens.");
        }
        if (package.Bindings.Select(binding => binding.Id).Distinct(StringComparer.Ordinal).Count() != package.Bindings.Count)
            return new(null, "Package binding ids must be unique.");
        if (package.Dependencies.Any(dependency =>
                dependency is null ||
                string.IsNullOrWhiteSpace(dependency.Id) ||
                !ScheduledJobPackageText.IsValidIdentifier(dependency.Id) ||
                !Enum.IsDefined(dependency.Kind) ||
                string.IsNullOrWhiteSpace(dependency.Name)))
        {
            return new(null, "Every package dependency requires an id and name.");
        }
        if (package.Dependencies.Select(dependency => dependency.Id).Distinct(StringComparer.Ordinal).Count() != package.Dependencies.Count)
            return new(null, "Package dependency ids must be unique.");
        if (package.Jobs.Select(job => job.PortableJobId).Distinct(StringComparer.Ordinal).Count() != package.Jobs.Count)
            return new(null, "Portable job ids must be unique.");

        var bindingIds = package.Bindings.Select(binding => binding.Id).ToHashSet(StringComparer.Ordinal);
        var dependencyIds = package.Dependencies.Select(dependency => dependency.Id).ToHashSet(StringComparer.Ordinal);
        var dependencyWithUnknownBinding = package.Dependencies
            .FirstOrDefault(dependency =>
                dependency.BindingId is not null &&
                !bindingIds.Contains(dependency.BindingId));
        if (dependencyWithUnknownBinding is not null)
            return new(null, $"Dependency '{dependencyWithUnknownBinding.Id}' references an unknown binding.");

        foreach (var job in package.Jobs)
        {
            if (string.IsNullOrWhiteSpace(job.PortableJobId))
                return new(null, "Every packaged job requires a portable job id.");
            if (job.Definition is null)
                return new(null, $"Packaged job '{job.PortableJobId}' has no definition.");
            if (string.IsNullOrWhiteSpace(job.Definition.Name))
                return new(null, $"Packaged job '{job.PortableJobId}' requires a name.");
            if (!ScheduledJobPackageText.IsValidTaskName(job.Definition.TaskName))
                return new(null, $"Packaged job '{job.PortableJobId}' has an invalid task name.");
            if (job.Definition.PromptTemplate is null ||
                job.Definition.PromptTemplate.Length > MaxPromptChars)
            {
                return new(null, $"Packaged job '{job.PortableJobId}' has an invalid prompt.");
            }
            if (job.Definition.Cadence is null ||
                job.Definition.Cadence.Days is null ||
                job.Definition.Skills is null)
            {
                return new(null, $"Packaged job '{job.PortableJobId}' has incomplete cadence or skill metadata.");
            }
            if (job.Definition.WorkingDirectoryBindingId is not null &&
                !bindingIds.Contains(job.Definition.WorkingDirectoryBindingId))
            {
                return new(null, $"Packaged job '{job.PortableJobId}' references an unknown working-directory binding.");
            }

            var unknownBinding = ScheduledJobPackageText.RequiredBindingIds(job.Definition)
                .FirstOrDefault(bindingId => !bindingIds.Contains(bindingId));
            if (unknownBinding is not null)
                return new(null, $"Packaged job '{job.PortableJobId}' references unknown binding '{unknownBinding}'.");
            var unknownDependency = job.Definition.Skills
                .FirstOrDefault(skill => !dependencyIds.Contains(skill.DependencyId));
            if (unknownDependency is not null)
                return new(null, $"Packaged job '{job.PortableJobId}' references an unknown skill dependency.");

            var actualFingerprint = FingerprintDefinition(job.Definition);
            if (!string.Equals(
                    actualFingerprint,
                    job.DefinitionFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new(null, $"Packaged job '{job.PortableJobId}' definition fingerprint does not match its content.");
            }
        }

        return new(package, null);
    }

    public static string FingerprintDefinition(ScheduledJobPortableDefinition definition) =>
        Sha256(JsonSerializer.Serialize(
            definition,
            ScheduledJobPackageJsonContext.Default.ScheduledJobPortableDefinition));

    public static string PreviewFingerprint(
        string packageFingerprint,
        string destinationTimeZone,
        IReadOnlyList<ScheduledJobPackageBindingPreview> bindings,
        IReadOnlyList<ScheduledJobPackageJobPreview> jobs,
        IReadOnlyDictionary<string, ScheduledJobPackageJobOptions> options)
    {
        var builder = new StringBuilder(packageFingerprint)
            .Append('|')
            .Append(destinationTimeZone);
        foreach (var binding in bindings.OrderBy(binding => binding.Id, StringComparer.Ordinal))
        {
            builder.Append('|')
                .Append(binding.Id)
                .Append('=')
                .Append(binding.ResolvedValue)
                .Append(':')
                .Append(binding.Error);
        }

        foreach (var job in jobs.OrderBy(job => job.PortableJobId, StringComparer.Ordinal))
        {
            var jobOptions = options[job.PortableJobId];
            builder.Append('|')
                .Append(job.PortableJobId)
                .Append(':')
                .Append(job.TargetTaskName)
                .Append(':')
                .Append(job.Status)
                .Append(':')
                .Append(jobOptions.AllowDuplicate)
                .Append(':')
                .Append(jobOptions.Skip);
            foreach (var finding in job.Findings.OrderBy(finding => finding.Code, StringComparer.Ordinal))
                builder.Append(':').Append(finding.Code).Append('=').Append(finding.Message);
        }

        return Sha256(builder.ToString());
    }

    public static string StablePackageId(
        IReadOnlyList<string> ids,
        ScheduledJobPackageProfile profile)
    {
        var source = $"{profile}:{string.Join("|", ids.OrderBy(id => id, StringComparer.Ordinal))}";
        return $"narnia-{Sha256(source)[..24].ToLowerInvariant()}";
    }

    public static string PackageFingerprint(string packageJson) =>
        Sha256(packageJson);

    public static string CurrentVersion() =>
        typeof(ScheduledJobPackageService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(ScheduledJobPackageService).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal sealed record ScheduledJobPackageParseResult(
    ScheduledJobPackage? Package,
    string? Error);
