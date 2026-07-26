using System.Text;
using System.Text.RegularExpressions;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

internal static partial class ScheduledJobPackageText
{
    private const string BindingTokenPrefix = "{{narnia:";
    private const string BindingTokenSuffix = "}}";

    public static string BindingToken(string id) =>
        $"{BindingTokenPrefix}{id}{BindingTokenSuffix}";

    public static string? RenderText(
        string? value,
        IReadOnlyDictionary<string, string> bindings)
    {
        if (value is null)
            return null;

        var result = value;
        foreach (var binding in bindings)
            result = result.Replace(BindingToken(binding.Key), binding.Value, StringComparison.Ordinal);
        return result;
    }

    public static string ReplacePathWithToken(
        string text,
        string path,
        string token)
    {
        var normalizedPath = path.Length > 3
            ? path.TrimEnd('\\', '/')
            : path;
        var hasTrailingSeparator = normalizedPath.EndsWith('\\') || normalizedPath.EndsWith('/');
        var boundary = hasTrailingSeparator
            ? ""
            : """(?=$|[\\/\s"',.;:)\]}])""";
        var pattern = $"""(?<![A-Za-z0-9_]){Regex.Escape(normalizedPath)}{boundary}""";
        return Regex.Replace(
            text,
            pattern,
            _ => token,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static IReadOnlyList<string> FindAbsolutePaths(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in QuotedAbsolutePathRegex().Matches(text))
            result.Add(match.Groups["path"].Value.TrimEnd('.', ':'));
        foreach (Match match in UnquotedAbsolutePathRegex().Matches(text))
            result.Add(match.Groups["path"].Value.TrimEnd('.', ':'));
        return result
            .Where(path => !path.Contains(BindingTokenPrefix, StringComparison.Ordinal))
            .ToArray();
    }

    public static IReadOnlyList<string> RequiredBindingIds(
        ScheduledJobPortableDefinition definition)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (definition.WorkingDirectoryBindingId is not null)
            result.Add(definition.WorkingDirectoryBindingId);
        AddBindingTokens(definition.Description, result);
        AddBindingTokens(definition.PromptTemplate, result);
        AddBindingTokens(definition.AllowFlags, result);
        AddBindingTokens(definition.CopilotArgs, result);
        return result.ToArray();
    }

    public static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousHyphen = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousHyphen = false;
            }
            else if (!previousHyphen)
            {
                builder.Append('-');
                previousHyphen = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return result.Length == 0 ? "value" : result;
    }

    public static bool IsValidTaskName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 240 &&
        !value.Any(character =>
            character is '\\' or '/' ||
            char.IsControl(character));

    public static bool IsPathCoveredByBinding(
        string path,
        string bindingValue)
    {
        var normalizedBinding = bindingValue.Length > 3
            ? bindingValue.TrimEnd('\\', '/')
            : bindingValue;
        if (!path.StartsWith(normalizedBinding, StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.Length == normalizedBinding.Length)
            return true;
        if (normalizedBinding.EndsWith('\\') || normalizedBinding.EndsWith('/'))
            return true;

        return path[normalizedBinding.Length] is '\\' or '/';
    }

    public static bool IsValidIdentifier(string value) =>
        BindingIdRegex().IsMatch(value);

    public static bool ContainsCredentialLikeLiteral(string value) =>
        SecretLikeValueRegex().IsMatch(value);

    private static void AddBindingTokens(
        string? value,
        ISet<string> result)
    {
        if (value is null)
            return;
        foreach (Match match in BindingTokenRegex().Matches(value))
            result.Add(match.Groups["id"].Value);
    }

    [GeneratedRegex("""(?<quote>["'])(?<path>(?:[A-Za-z]:\\|\\\\)[^"'\r\n]+)\k<quote>""")]
    private static partial Regex QuotedAbsolutePathRegex();

    [GeneratedRegex("""(?<![A-Za-z0-9_])(?<path>(?:[A-Za-z]:\\|\\\\)[^"'\s\r\n,;)\]}]+)""")]
    private static partial Regex UnquotedAbsolutePathRegex();

    [GeneratedRegex(@"\{\{narnia:(?<id>[a-z0-9-]+)\}\}")]
    private static partial Regex BindingTokenRegex();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$")]
    private static partial Regex BindingIdRegex();

    [GeneratedRegex(
        """(?i)(?:api[_-]?key|token|password|secret)\s*[:=]\s*["']?[A-Za-z0-9/+_.-]{8,}""")]
    private static partial Regex SecretLikeValueRegex();
}
