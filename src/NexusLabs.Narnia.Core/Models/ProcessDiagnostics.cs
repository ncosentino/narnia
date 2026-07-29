namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A point-in-time resource record for one operating-system process.
/// </summary>
/// <param name="ProcessId">The operating-system process identifier.</param>
/// <param name="ParentProcessId">The process identifier recorded as the parent.</param>
/// <param name="Name">The executable image name.</param>
/// <param name="StartedAt">The process start time, when available.</param>
/// <param name="TotalProcessorTime">Cumulative kernel and user processor time.</param>
/// <param name="WorkingSetBytes">The current working-set size in bytes.</param>
/// <param name="PrivateBytes">The current private committed memory in bytes.</param>
public sealed record ProcessResourceRecord(
    int ProcessId,
    int ParentProcessId,
    string Name,
    DateTimeOffset? StartedAt,
    TimeSpan TotalProcessorTime,
    long WorkingSetBytes,
    long PrivateBytes);

/// <summary>
/// A raw process-resource sample captured by a platform-specific provider.
/// </summary>
/// <param name="IsAvailable">Whether the provider returned a usable process sample.</param>
/// <param name="UnavailableReason">The reason diagnostics are unavailable, or <c>null</c>.</param>
/// <param name="CapturedAt">The wall-clock capture time used for display.</param>
/// <param name="MonotonicTime">A monotonic timestamp used for CPU delta calculations.</param>
/// <param name="LogicalProcessorCount">The logical processor count used to normalize CPU usage.</param>
/// <param name="Processes">The processes present in the sample.</param>
public sealed record ProcessResourceSnapshot(
    bool IsAvailable,
    string? UnavailableReason,
    DateTimeOffset CapturedAt,
    TimeSpan MonotonicTime,
    int LogicalProcessorCount,
    IReadOnlyList<ProcessResourceRecord> Processes);

/// <summary>
/// Resource usage for one process or a deduplicated set of processes.
/// </summary>
/// <param name="CpuPercent">
/// CPU usage normalized to total machine capacity, or <c>null</c> until a prior sample exists.
/// </param>
/// <param name="CpuSampledProcessCount">The number of processes with a usable CPU delta.</param>
/// <param name="ProcessCount">The number of unique processes represented by the usage.</param>
/// <param name="PrivateBytes">Summed private committed memory in bytes.</param>
/// <param name="WorkingSetBytes">
/// Summed working-set memory in bytes. Shared pages can be counted more than once.
/// </param>
public sealed record ProcessUsage(
    double? CpuPercent,
    int CpuSampledProcessCount,
    int ProcessCount,
    long PrivateBytes,
    long WorkingSetBytes);

/// <summary>
/// Identity and own resource usage for a process in a launch chain.
/// </summary>
/// <param name="ProcessId">The operating-system process identifier.</param>
/// <param name="ParentProcessId">The recorded parent process identifier.</param>
/// <param name="Name">The executable image name.</param>
/// <param name="StartedAt">The process start time, when available.</param>
/// <param name="Usage">Resource usage for this process only.</param>
public sealed record ProcessDescriptor(
    int ProcessId,
    int ParentProcessId,
    string Name,
    DateTimeOffset? StartedAt,
    ProcessUsage Usage);

/// <summary>
/// A process and its validated descendants at one point in time.
/// </summary>
/// <param name="ProcessId">The operating-system process identifier.</param>
/// <param name="ParentProcessId">The recorded parent process identifier.</param>
/// <param name="Name">The executable image name.</param>
/// <param name="StartedAt">The process start time, when available.</param>
/// <param name="OwnUsage">Resource usage for this process only.</param>
/// <param name="TreeUsage">Deduplicated resource usage for this process and its descendants.</param>
/// <param name="Children">Validated direct child processes.</param>
public sealed record ProcessTreeNode(
    int ProcessId,
    int ParentProcessId,
    string Name,
    DateTimeOffset? StartedAt,
    ProcessUsage OwnUsage,
    ProcessUsage TreeUsage,
    IReadOnlyList<ProcessTreeNode> Children);

/// <summary>
/// Session metadata associated with a live Copilot runtime.
/// </summary>
/// <param name="SessionId">The Copilot session identifier.</param>
/// <param name="Summary">The effective session summary or Narnia alias.</param>
/// <param name="Repository">The effective remote repository.</param>
/// <param name="Branch">The effective branch.</param>
/// <param name="Cwd">The effective recorded working directory.</param>
/// <param name="IsPrimary">
/// Whether this is the top-level tab session rather than a nested or background-agent session
/// sharing the same runtime process.
/// </param>
public sealed record ProcessSessionReference(
    string SessionId,
    string? Summary,
    string? Repository,
    string? Branch,
    string? Cwd,
    bool IsPrimary);

/// <summary>
/// Diagnostics for one live <c>copilot.exe</c> runtime and its descendants.
/// </summary>
/// <param name="CopilotProcessId">The live Copilot runtime process identifier.</param>
/// <param name="ShellProcessId">The owning shell process identifier, when recognized.</param>
/// <param name="TerminalProcessId">The owning Windows Terminal process identifier, when present.</param>
/// <param name="StartedAt">The Copilot runtime start time, when available.</param>
/// <param name="LaunchChain">
/// Validated ancestors between the terminal and Copilot runtime, ordered from shell to launcher.
/// </param>
/// <param name="RuntimeTree">The Copilot runtime and every validated descendant process.</param>
/// <param name="Sessions">
/// Sessions holding a live lock for this runtime. Usage belongs to the runtime once, even when
/// multiple provisional, nested, or background session-state folders share it.
/// </param>
public sealed record CopilotRuntimeDiagnostics(
    int CopilotProcessId,
    int? ShellProcessId,
    int? TerminalProcessId,
    DateTimeOffset? StartedAt,
    IReadOnlyList<ProcessDescriptor> LaunchChain,
    ProcessTreeNode RuntimeTree,
    IReadOnlyList<ProcessSessionReference> Sessions);

/// <summary>
/// Diagnostics for one Windows Terminal process and its complete validated process tree.
/// </summary>
/// <param name="TerminalProcessId">The Windows Terminal process identifier.</param>
/// <param name="StartedAt">The terminal process start time, when available.</param>
/// <param name="ProcessTree">The terminal process and every validated descendant.</param>
/// <param name="OtherUsage">
/// Terminal-tree usage not contained in a mapped Copilot runtime subtree. This includes shells,
/// launchers, non-Copilot tabs, and other terminal-owned processes.
/// </param>
/// <param name="Runtimes">Mapped Copilot runtimes owned by this terminal.</param>
public sealed record TerminalProcessDiagnostics(
    int TerminalProcessId,
    DateTimeOffset? StartedAt,
    ProcessTreeNode ProcessTree,
    ProcessUsage OtherUsage,
    IReadOnlyList<CopilotRuntimeDiagnostics> Runtimes);

/// <summary>
/// A live, read-only diagnostic view of process resource usage and Copilot session ownership.
/// </summary>
/// <param name="IsAvailable">Whether process diagnostics are available.</param>
/// <param name="UnavailableReason">The reason diagnostics are unavailable, or <c>null</c>.</param>
/// <param name="CapturedAt">The most recent process sample time.</param>
/// <param name="SampleDurationSeconds">
/// Seconds between CPU samples, or <c>null</c> when this is the first sample.
/// </param>
/// <param name="LogicalProcessorCount">Logical processors used to normalize CPU percentages.</param>
/// <param name="TopologySignature">
/// Stable signature of terminal, Copilot runtime, and mapped-session ownership used by the UI.
/// </param>
/// <param name="ProcessTreeSignature">
/// Stable signature of rendered process identities and parent links. The UI confirms changes
/// across samples before reloading so persistent children appear without reacting to every
/// short-lived command.
/// </param>
/// <param name="ProcessTreeIdentities">
/// Stable process-identity and parent-edge tokens used to confirm individual child additions
/// and removals across browser polls.
/// </param>
/// <param name="SampledProcessesUsage">
/// Usage across sampled non-idle processes. CPU is an estimate and can omit processes that exit
/// between samples.
/// </param>
/// <param name="CopilotRuntimeUsage">
/// Deduplicated usage across every Copilot runtime and its descendants.
/// </param>
/// <param name="TerminalUsage">
/// Deduplicated usage across every Windows Terminal process tree.
/// </param>
/// <param name="MappedSessionCount">Unique live session identifiers mapped to Copilot runtimes.</param>
/// <param name="Terminals">Windows Terminal process groups, including terminals without Copilot.</param>
/// <param name="OrphanedRuntimes">Copilot runtimes not attributable to Windows Terminal.</param>
public sealed record ProcessDiagnosticsSnapshot(
    bool IsAvailable,
    string? UnavailableReason,
    DateTimeOffset CapturedAt,
    double? SampleDurationSeconds,
    int LogicalProcessorCount,
    string TopologySignature,
    string ProcessTreeSignature,
    IReadOnlyList<string> ProcessTreeIdentities,
    ProcessUsage SampledProcessesUsage,
    ProcessUsage CopilotRuntimeUsage,
    ProcessUsage TerminalUsage,
    int MappedSessionCount,
    IReadOnlyList<TerminalProcessDiagnostics> Terminals,
    IReadOnlyList<CopilotRuntimeDiagnostics> OrphanedRuntimes);
