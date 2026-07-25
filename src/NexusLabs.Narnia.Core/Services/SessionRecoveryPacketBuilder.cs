using Microsoft.Data.Sqlite;
using System.IO.Abstractions;
using System.Text;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Builds bounded recovery archives and high-signal bootstrap context.</summary>
public sealed class SessionRecoveryPacketBuilder(
    ISessionRepository sessionRepository,
    ISessionOverridesRepository overridesRepository,
    IWorkspaceReader workspaceReader,
    ISessionTaskStateReader taskStateReader,
    NarniaOptions options,
    IFileSystem fileSystem) : ISessionRecoveryPacketBuilder
{
    private const int FullPacketCharacterLimit = 1_000_000;
    private const int BootstrapCharacterLimit = 70_000;
    private const int FullConversationTailCount = 250;
    private const int BootstrapConversationTailCount = 24;
    private const int MaximumRecoveredFieldCharacters = 120_000;

    /// <inheritdoc />
    public async ValueTask<SessionRecoveryPacketBuildResult> BuildAsync(
        string sourceSessionId,
        string replacementSessionId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(sourceSessionId, out _) ||
            !Guid.TryParse(replacementSessionId, out _))
        {
            return Failure("Source and replacement session identifiers must be GUIDs.");
        }

        try
        {
            var session = await sessionRepository.GetByIdAsync(sourceSessionId, ct);
            if (session is null)
                return Failure("The source session is not available in the Copilot session index.");

            var sessionOverride = await overridesRepository.GetOverrideAsync(sourceSessionId, ct);
            var checkpoints = await sessionRepository.GetCheckpointsAsync(sourceSessionId, ct);
            var workspace = workspaceReader.ReadWorkspace(sourceSessionId);
            var taskState = taskStateReader.Read(sourceSessionId);
            var turns = await ReadSelectedTurnsAsync(session, ct);
            var packet = BuildFullPacket(
                sourceSessionId,
                replacementSessionId,
                session,
                sessionOverride,
                workspace,
                checkpoints,
                taskState,
                turns);
            var bootstrap = BuildBootstrapPrompt(
                sourceSessionId,
                replacementSessionId,
                session,
                sessionOverride,
                workspace,
                checkpoints,
                taskState,
                turns);

            var recoveryRoot = fileSystem.Path.GetFullPath(options.RecoveryDirectory)
                .TrimEnd(
                    fileSystem.Path.DirectorySeparatorChar,
                    fileSystem.Path.AltDirectorySeparatorChar);
            var recoveryDirectory = fileSystem.Path.GetFullPath(
                fileSystem.Path.Combine(recoveryRoot, replacementSessionId));
            if (!string.Equals(
                    fileSystem.Path.GetDirectoryName(recoveryDirectory),
                    recoveryRoot,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return Failure("The recovery packet path does not resolve beneath Narnia's recovery directory.");
            }

            var packetPath = fileSystem.Path.Combine(recoveryDirectory, "recovery.md");
            var temporaryPath = packetPath + ".tmp";
            try
            {
                fileSystem.Directory.CreateDirectory(recoveryDirectory);
                fileSystem.File.WriteAllText(
                    temporaryPath,
                    packet.Content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (fileSystem.File.Exists(packetPath))
                    fileSystem.File.Delete(packetPath);
                fileSystem.File.Move(temporaryPath, packetPath);
            }
            finally
            {
                if (fileSystem.File.Exists(temporaryPath))
                {
                    try
                    {
                        fileSystem.File.Delete(temporaryPath);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            return new SessionRecoveryPacketBuildResult(
                true,
                packetPath,
                Encoding.UTF8.GetByteCount(packet.Content),
                packet.Truncated || turns.Truncated,
                bootstrap.Content,
                null);
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return Failure($"Narnia could not build the recovery packet: {exception.Message}");
        }
    }

    private async ValueTask<SelectedTurns> ReadSelectedTurnsAsync(
        Session session,
        CancellationToken ct)
    {
        if (session.TurnCount <= FullConversationTailCount)
        {
            var all = await sessionRepository.GetTurnsAsync(
                session.Id,
                0,
                Math.Max(session.TurnCount, 1),
                ct);
            return new SelectedTurns(all, false);
        }

        var first = await sessionRepository.GetTurnsAsync(session.Id, 0, 5, ct);
        var tailOffset = Math.Max(5, session.TurnCount - FullConversationTailCount);
        var tail = await sessionRepository.GetTurnsAsync(
            session.Id,
            tailOffset,
            FullConversationTailCount,
            ct);
        return new SelectedTurns([.. first, .. tail], true);
    }

    private static BuiltText BuildFullPacket(
        string sourceSessionId,
        string replacementSessionId,
        Session session,
        SessionOverride? sessionOverride,
        WorkspaceInfo workspace,
        IReadOnlyList<Checkpoint> checkpoints,
        SessionTaskState taskState,
        SelectedTurns turns)
    {
        var text = new BoundedText(FullPacketCharacterLimit);
        text.AppendLine("# Narnia Session Recovery Packet");
        text.AppendLine();
        text.AppendLine($"- Source session: `{sourceSessionId}`");
        text.AppendLine($"- Recovered successor: `{replacementSessionId}`");
        text.AppendLine($"- Effective name: {session.Summary ?? "(unnamed)"}");
        text.AppendLine($"- Repository: {session.Repository ?? "(unknown)"}");
        text.AppendLine($"- Branch: {session.Branch ?? "(unknown)"}");
        text.AppendLine($"- Working directory: {session.Cwd ?? "(unknown)"}");
        text.AppendLine($"- Created: {session.CreatedAt:o}");
        text.AppendLine($"- Last indexed update: {session.UpdatedAt:o}");
        text.AppendLine($"- Indexed turns: {session.TurnCount}");
        text.AppendLine($"- Indexed checkpoints: {session.CheckpointCount}");
        text.AppendLine();
        text.AppendLine(
            "The original Copilot session remains untouched. This packet is Narnia-owned and preserves recoverable history for the successor.");

        var metadata = new BoundedText(60_000);
        AppendNarniaMetadata(metadata, sessionOverride);
        text.Append(metadata.Content);
        AppendWorkspaceMetadata(text, workspace);
        var conversation = new BoundedText(450_000);
        AppendConversation(conversation, turns.Turns, turns.Truncated);
        text.Append(conversation.Content);
        var checkpointText = new BoundedText(300_000);
        AppendCheckpoints(checkpointText, checkpoints, includeFullHistory: true);
        text.Append(checkpointText.Content);
        var tasks = new BoundedText(160_000);
        AppendTasks(tasks, taskState, includeCompleted: true);
        text.Append(tasks.Content);
        var artifacts = new BoundedText(20_000);
        AppendArtifacts(artifacts, workspace.ArtifactFiles);
        text.Append(artifacts.Content);

        return new BuiltText(
            text.Content,
            text.Truncated ||
            metadata.Truncated ||
            conversation.Truncated ||
            checkpointText.Truncated ||
            tasks.Truncated ||
            artifacts.Truncated);
    }

    private static BoundedText BuildBootstrapPrompt(
        string sourceSessionId,
        string replacementSessionId,
        Session session,
        SessionOverride? sessionOverride,
        WorkspaceInfo workspace,
        IReadOnlyList<Checkpoint> checkpoints,
        SessionTaskState taskState,
        SelectedTurns turns)
    {
        var text = new BoundedText(BootstrapCharacterLimit);
        text.AppendLine(
            "Narnia created this valid successor because the original Copilot session could not be resumed safely.");
        text.AppendLine($"Source session: {sourceSessionId}");
        text.AppendLine($"Successor session: {replacementSessionId}");
        text.AppendLine();
        text.AppendLine(
            "For this bootstrap turn, do not run tools, edit files, or continue implementation. Synthesize the recovered material into a concise working-state handoff: goals, completed work, decisions, current repository/branch, unresolved tasks, risks, and important files. End by waiting for the user's direction.");
        text.AppendLine();
        text.AppendLine(
            $"Exact archived history remains available through Narnia's `get_session_recovery_packet` MCP tool using session id `{replacementSessionId}`.");
        text.AppendLine();
        text.AppendLine("## Session identity");
        text.AppendLine($"- Name: {session.Summary ?? "(unnamed)"}");
        text.AppendLine($"- Repository: {session.Repository ?? "(unknown)"}");
        text.AppendLine($"- Branch: {session.Branch ?? "(unknown)"}");
        text.AppendLine($"- Working directory: {session.Cwd ?? "(unknown)"}");
        if (!string.IsNullOrWhiteSpace(sessionOverride?.Notes))
        {
            text.AppendLine(
                $"- Narnia notes: {TruncateInline(sessionOverride.Notes, 5_000)}");
        }
        if (workspace.IsNestedAgent)
        {
            text.AppendLine(
                $"- Original nested-agent parent task/session: {workspace.ParentTaskId ?? "(unknown)"} / {workspace.ParentSessionId ?? "(unknown)"}");
        }

        var checkpointText = new BoundedText(20_000);
        AppendCheckpoints(checkpointText, checkpoints, includeFullHistory: false);
        text.Append(checkpointText.Content);
        var bootstrapTurns = turns.Turns
            .Take(1)
            .Concat(turns.Turns.TakeLast(BootstrapConversationTailCount))
            .DistinctBy(turn => turn.Id)
            .OrderBy(turn => turn.TurnIndex)
            .ToArray();
        var conversation = new BoundedText(35_000);
        AppendConversation(conversation, bootstrapTurns, turns.Truncated);
        text.Append(conversation.Content);
        var tasks = new BoundedText(10_000);
        AppendTasks(tasks, taskState, includeCompleted: false);
        text.Append(tasks.Content);
        var artifacts = new BoundedText(5_000);
        AppendArtifacts(artifacts, workspace.ArtifactFiles);
        text.Append(artifacts.Content);
        return text.Build();
    }

    private static void AppendNarniaMetadata(BoundedText text, SessionOverride? sessionOverride)
    {
        text.AppendLine();
        text.AppendLine("## Narnia metadata");
        if (sessionOverride is null)
        {
            text.AppendLine("- No saved Narnia override metadata.");
            return;
        }

        text.AppendLine($"- Alias: {sessionOverride.DisplayName ?? "(none)"}");
        text.AppendLine($"- Favorite: {(sessionOverride.IsFavorite ? "yes" : "no")}");
        text.AppendLine($"- Archived: {(sessionOverride.IsArchived ? "yes" : "no")}");
        text.AppendLine($"- Repository override: {sessionOverride.Repository ?? "(none)"}");
        text.AppendLine($"- Branch override: {sessionOverride.Branch ?? "(none)"}");
        text.AppendLine($"- Preferred path: {sessionOverride.LocalPath ?? "(none)"}");
        text.AppendLine($"- Terminal title: {sessionOverride.TerminalTitle ?? "(none)"}");
        AppendLabeledContent(text, "Notes", sessionOverride.Notes ?? "(none)");
    }

    private static void AppendWorkspaceMetadata(BoundedText text, WorkspaceInfo workspace)
    {
        text.AppendLine();
        text.AppendLine("## Copilot workspace metadata");
        text.AppendLine($"- Copilot name: {workspace.Name ?? "(none)"}");
        text.AppendLine($"- User named: {(workspace.IsUserNamed ? "yes" : "no")}");
        text.AppendLine($"- Git root: {workspace.GitRoot ?? "(none)"}");
        text.AppendLine($"- Nested agent: {(workspace.IsNestedAgent ? "yes" : "no")}");
        if (workspace.IsNestedAgent)
        {
            text.AppendLine($"- Parent task id: {workspace.ParentTaskId ?? "(none)"}");
            text.AppendLine($"- Parent session id: {workspace.ParentSessionId ?? "(none)"}");
        }
    }

    private static void AppendCheckpoints(
        BoundedText text,
        IReadOnlyList<Checkpoint> checkpoints,
        bool includeFullHistory)
    {
        text.AppendLine();
        text.AppendLine($"## Checkpoints ({checkpoints.Count})");
        if (checkpoints.Count == 0)
        {
            text.AppendLine("- None recorded.");
            return;
        }

        var selected = includeFullHistory
            ? checkpoints.OrderByDescending(checkpoint => checkpoint.CheckpointNumber).ToArray()
            : checkpoints.TakeLast(1).ToArray();

        foreach (var checkpoint in selected)
        {
            text.AppendLine();
            text.AppendLine(
                $"### #{checkpoint.CheckpointNumber}: {checkpoint.Title ?? "(untitled)"}");
            text.AppendLine($"Created: {checkpoint.CreatedAt:o}");
            AppendLabeledContent(text, "Overview", checkpoint.Overview);
            AppendLabeledContent(text, "Work done", checkpoint.WorkDone);
            AppendLabeledContent(text, "Next steps", checkpoint.NextSteps);
            AppendLabeledContent(text, "Important files", checkpoint.ImportantFiles);
            AppendLabeledContent(text, "Technical details", checkpoint.TechnicalDetails);
            if (includeFullHistory)
                AppendLabeledContent(text, "History", checkpoint.History);
        }

        if (!includeFullHistory && checkpoints.Count > 1)
        {
            text.AppendLine();
            text.AppendLine("### Earlier checkpoint timeline");
            foreach (var checkpoint in checkpoints
                         .Take(checkpoints.Count - 1)
                         .OrderByDescending(checkpoint => checkpoint.CheckpointNumber))
            {
                text.AppendLine(
                    $"- #{checkpoint.CheckpointNumber} {checkpoint.Title ?? "(untitled)"}: {checkpoint.Overview ?? "(no overview)"}");
            }
        }
    }

    private static void AppendTasks(
        BoundedText text,
        SessionTaskState taskState,
        bool includeCompleted)
    {
        text.AppendLine();
        text.AppendLine($"## Workspace tasks ({taskState.Todos.Count})");
        if (!string.IsNullOrWhiteSpace(taskState.Error))
            text.AppendLine($"> {taskState.Error}");
        if (taskState.Todos.Count == 0)
        {
            text.AppendLine("- None recorded.");
            return;
        }

        var selected = includeCompleted
            ? taskState.Todos
            : taskState.Todos
                .Where(todo => !string.Equals(todo.Status, "done", StringComparison.OrdinalIgnoreCase))
                .Concat(taskState.Todos
                    .Where(todo => string.Equals(todo.Status, "done", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(10))
                .ToArray();
        foreach (var todo in selected)
        {
            text.AppendLine();
            text.AppendLine($"### [{todo.Status}] {todo.Title} (`{todo.Id}`)");
            if (!string.IsNullOrWhiteSpace(todo.Description))
                text.AppendLine(todo.Description);
            var dependencies = taskState.Dependencies
                .Where(dependency => string.Equals(
                    dependency.TaskId,
                    todo.Id,
                    StringComparison.Ordinal))
                .Select(dependency => dependency.DependsOn)
                .ToArray();
            if (dependencies.Length > 0)
                text.AppendLine($"Depends on: {string.Join(", ", dependencies.Select(id => $"`{id}`"))}");
        }
    }

    private static void AppendConversation(
        BoundedText text,
        IReadOnlyList<Turn> turns,
        bool omittedMiddle)
    {
        text.AppendLine();
        text.AppendLine($"## Conversation turns included, newest first ({turns.Count})");
        if (turns.Count == 0)
        {
            text.AppendLine("- None recorded.");
            return;
        }

        if (omittedMiddle)
        {
            text.AppendLine(
                "> Middle turns were omitted to keep the recovery packet bounded; the earliest selected turn and most recent turns are retained.");
        }

        foreach (var turn in turns.OrderByDescending(turn => turn.TurnIndex))
        {
            text.AppendLine();
            text.AppendLine($"### Turn {turn.TurnIndex} ({turn.Timestamp:o})");
            AppendLabeledContent(text, "User", turn.UserMessage);
            AppendLabeledContent(text, "Assistant", turn.AssistantResponse);
        }
    }

    private static void AppendArtifacts(BoundedText text, IReadOnlyList<string> artifacts)
    {
        text.AppendLine();
        text.AppendLine($"## Session artifact names ({artifacts.Count})");
        if (artifacts.Count == 0)
        {
            text.AppendLine("- None recorded at the top level.");
            return;
        }

        foreach (var artifact in artifacts.Order(StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"- `{artifact}`");
    }

    private static void AppendLabeledContent(
        BoundedText text,
        string label,
        string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;
        var boundedContent = content.Length <= MaximumRecoveredFieldCharacters
            ? content
            : content[..MaximumRecoveredFieldCharacters] +
              Environment.NewLine +
              "[Content truncated by Narnia's per-field recovery limit.]";
        if (content.Length > MaximumRecoveredFieldCharacters)
            text.MarkTruncated();
        text.AppendLine();
        text.AppendLine($"**{label}**");
        text.AppendLine(boundedContent);
    }

    private static SessionRecoveryPacketBuildResult Failure(string error) =>
        new(false, null, 0, false, null, error);

    private static string TruncateInline(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters] + " [truncated]";

    private sealed record BuiltText(string Content, bool Truncated);

    private sealed record SelectedTurns(IReadOnlyList<Turn> Turns, bool Truncated);

    private sealed class BoundedText(int limit)
    {
        private const string TruncationMarker =
            "\n\n> Content truncated by Narnia's bounded recovery limit.\n";
        private readonly StringBuilder _builder = new(Math.Min(limit, 64 * 1024));
        private bool _limitReached;
        private bool _contentOmitted;

        public bool Truncated => _limitReached || _contentOmitted;

        public void MarkTruncated() => _contentOmitted = true;

        public void AppendLine() => Append(Environment.NewLine);

        public void AppendLine(string value)
        {
            Append(value);
            Append(Environment.NewLine);
        }

        public void Append(string value)
        {
            if (_limitReached || string.IsNullOrEmpty(value))
                return;

            var remaining = limit - _builder.Length;
            if (remaining <= 0)
            {
                _limitReached = true;
                return;
            }

            if (value.Length <= remaining)
            {
                _builder.Append(value);
                return;
            }

            _builder.Append(value.AsSpan(0, remaining));
            _limitReached = true;
        }

        public BoundedText Build() => this;

        public string Content
        {
            get
            {
                if (!Truncated)
                    return _builder.ToString();

                var retained = Math.Max(0, limit - TruncationMarker.Length);
                return _builder.ToString(0, Math.Min(retained, _builder.Length)) +
                       TruncationMarker;
            }
        }
    }
}
