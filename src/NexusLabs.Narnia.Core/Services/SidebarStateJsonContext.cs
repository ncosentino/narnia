using System.Text.Json.Serialization;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// On-disk shape of a Copilot sidebar tab list. Property names must stay camelCase so a rewrite
/// produces a file Copilot can still read.
/// </summary>
internal sealed class SidebarStateDocument
{
    public int? SchemaVersion { get; set; }
    public string? Cwd { get; set; }
    public string[]? SessionIds { get; set; }
}

[JsonSerializable(typeof(SidebarStateDocument))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
internal sealed partial class SidebarStateJsonContext : JsonSerializerContext;
