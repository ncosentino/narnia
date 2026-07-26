using System.Text.Json.Serialization;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web;

[JsonSerializable(typeof(ScheduledJobPackageExportResult))]
[JsonSerializable(typeof(ScheduledJobPackagePreviewResult))]
[JsonSerializable(typeof(ScheduledJobPackageImportResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
internal sealed partial class SchedulePackageWebJsonContext : JsonSerializerContext;
