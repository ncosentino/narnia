using System.Text.Json.Serialization;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Mcp;

[JsonSerializable(typeof(Session))]
[JsonSerializable(typeof(SessionSummary))]
[JsonSerializable(typeof(SessionSummary[]))]
[JsonSerializable(typeof(Turn))]
[JsonSerializable(typeof(Turn[]))]
[JsonSerializable(typeof(Checkpoint))]
[JsonSerializable(typeof(Checkpoint[]))]
[JsonSerializable(typeof(SessionFile[]))]
[JsonSerializable(typeof(SessionRef[]))]
[JsonSerializable(typeof(SearchResult[]))]
[JsonSerializable(typeof(WorkspaceInfo))]
[JsonSerializable(typeof(ScheduleJobMcpDto))]
[JsonSerializable(typeof(ScheduleListMcpDto))]
[JsonSerializable(typeof(ScheduleCreateMcpDto))]
[JsonSerializable(typeof(ScheduleMutationMcpDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class McpJsonContext : JsonSerializerContext;
