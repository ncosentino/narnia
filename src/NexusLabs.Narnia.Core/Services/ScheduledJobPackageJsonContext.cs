using System.Text.Json.Serialization;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

[JsonSerializable(typeof(ScheduledJobPackage))]
[JsonSerializable(typeof(ScheduledJobPortableDefinition))]
[JsonSerializable(typeof(ScheduledJobPackagePreviewResult))]
[JsonSerializable(typeof(ScheduledJobPackageImportResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
internal sealed partial class ScheduledJobPackageJsonContext : JsonSerializerContext;
