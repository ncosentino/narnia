using System.ComponentModel;
using System.Net.Sockets;
using System.Text.Json;
using GitHub.Copilot;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>Deletes local sessions through the official GitHub Copilot SDK runtime.</summary>
public sealed class CopilotSdkSessionManager(
    INarniaSettingsRepository settings,
    NarniaOptions options,
    IPowerShellHostResolver powerShellHostResolver,
    ILogger<CopilotSdkSessionManager> logger) : ICopilotSessionManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CopilotSessionDeletionResult>> DeleteSessionsAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken ct)
    {
        var normalized = sessionIds
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Select(sessionId => sessionId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            return [];

        if (!TryResolveCopilotHome(out var copilotHome, out var pathError))
            return FailureResults(normalized, pathError!);

        var configuredCommand =
            await settings.GetAsync(CopilotSettingKeys.Command, ct) ??
            CopilotSettingKeys.DefaultCommand;
        if (!CopilotCommandParser.TryParse(configuredCommand, out var command, out var commandError))
            return FailureResults(normalized, commandError!);

        await _gate.WaitAsync(ct);
        try
        {
            CopilotClient? client = null;
            try
            {
                var connection = BuildConnection(command!);
                client = new CopilotClient(new CopilotClientOptions
                {
                    Connection = connection,
                    BaseDirectory = copilotHome,
                    EnableRemoteSessions = false,
                    UseLoggedInUser = true,
                    LogLevel = CopilotLogLevel.Error,
                });
                await client.StartAsync(ct);
                var available = (await client.ListSessionsAsync(null, ct))
                    .Select(session => session.SessionId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var results = new List<CopilotSessionDeletionResult>(normalized.Length);
                foreach (var sessionId in normalized)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!available.Contains(sessionId))
                    {
                        results.Add(new CopilotSessionDeletionResult(
                            sessionId,
                            false,
                            "Session is not available through the local Copilot SDK runtime."));
                        continue;
                    }

                    try
                    {
                        await client.DeleteSessionAsync(sessionId, ct);
                        results.Add(new CopilotSessionDeletionResult(sessionId, true, null));
                    }
                    catch (Exception exception) when (IsExpectedSdkException(exception))
                    {
                        results.Add(new CopilotSessionDeletionResult(
                            sessionId,
                            false,
                            exception.Message));
                    }
                }

                return results;
            }
            catch (Exception exception) when (IsExpectedSdkException(exception))
            {
                return FailureResults(normalized, exception.Message);
            }
            finally
            {
                if (client is not null)
                {
                    try
                    {
                        await client.DisposeAsync();
                    }
                    catch (Exception exception) when (IsExpectedSdkException(exception))
                    {
                        logger.LogWarning(
                            exception,
                            "Copilot SDK cleanup runtime did not shut down cleanly.");
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<CopilotRecoverySessionResult> CreateRecoverySessionAsync(
        CopilotRecoverySessionRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.SessionId, out _))
        {
            return new CopilotRecoverySessionResult(
                request.SessionId,
                false,
                "The replacement session identifier must be a GUID.");
        }
        if (string.IsNullOrWhiteSpace(request.BootstrapPrompt))
        {
            return new CopilotRecoverySessionResult(
                request.SessionId,
                false,
                "Recovery bootstrap context is empty.");
        }
        if (!TryResolveCopilotHome(out var copilotHome, out var pathError))
            return new CopilotRecoverySessionResult(request.SessionId, false, pathError);

        var configuredCommand =
            await settings.GetAsync(CopilotSettingKeys.Command, ct) ??
            CopilotSettingKeys.DefaultCommand;
        if (!CopilotCommandParser.TryParse(configuredCommand, out var command, out var commandError))
            return new CopilotRecoverySessionResult(request.SessionId, false, commandError);

        await _gate.WaitAsync(ct);
        try
        {
            CopilotClient? client = null;
            CopilotSession? session = null;
            try
            {
                client = new CopilotClient(new CopilotClientOptions
                {
                    Connection = BuildConnection(command!),
                    BaseDirectory = copilotHome,
                    Mode = CopilotClientMode.Empty,
                    EnableRemoteSessions = false,
                    UseLoggedInUser = true,
                    LogLevel = CopilotLogLevel.Error,
                });
                await client.StartAsync(ct);
                var available = await client.ListSessionsAsync(null, ct);
                if (available.Any(item => string.Equals(
                        item.SessionId,
                        request.SessionId,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return new CopilotRecoverySessionResult(
                        request.SessionId,
                        false,
                        "The replacement session identifier is already in use.");
                }

                session = await client.CreateSessionAsync(
                    new SessionConfig
                    {
                        SessionId = request.SessionId,
                        WorkingDirectory =
                            !string.IsNullOrWhiteSpace(request.WorkingDirectory) &&
                            Directory.Exists(request.WorkingDirectory)
                                ? request.WorkingDirectory
                                : null,
                        AvailableTools = [],
                        EnableSessionStore = true,
                        SkipCustomInstructions = true,
                        EnableConfigDiscovery = false,
                        EnableSkills = false,
                        Streaming = false,
                        ClientName = "narnia-session-recovery",
                    },
                    ct);
                var response = await session.SendAndWaitAsync(
                    new MessageOptions { Prompt = request.BootstrapPrompt },
                    TimeSpan.FromMinutes(5),
                    ct);
                if (response is null || string.IsNullOrWhiteSpace(response.Data.Content))
                {
                    return new CopilotRecoverySessionResult(
                        request.SessionId,
                        false,
                        "Copilot created the successor but did not return a recovery handoff.");
                }
                return new CopilotRecoverySessionResult(request.SessionId, true, null);
            }
            catch (Exception exception) when (IsExpectedSdkException(exception))
            {
                return new CopilotRecoverySessionResult(
                    request.SessionId,
                    false,
                    exception.Message);
            }
            finally
            {
                if (session is not null)
                {
                    try
                    {
                        await session.DisposeAsync();
                    }
                    catch (Exception exception) when (IsExpectedSdkException(exception))
                    {
                        logger.LogWarning(
                            exception,
                            "Recovered Copilot session {SessionId} did not disconnect cleanly.",
                            request.SessionId);
                    }
                }

                if (client is not null)
                {
                    try
                    {
                        await client.DisposeAsync();
                    }
                    catch (Exception exception) when (IsExpectedSdkException(exception))
                    {
                        logger.LogWarning(
                            exception,
                            "Copilot SDK recovery runtime did not shut down cleanly.");
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<CopilotSessionAvailabilityResult> CheckSessionAvailabilityAsync(
        string sessionId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(sessionId, out _))
        {
            return new CopilotSessionAvailabilityResult(
                sessionId,
                false,
                false,
                "The session identifier must be a GUID.");
        }
        if (!TryResolveCopilotHome(out var copilotHome, out var pathError))
        {
            return new CopilotSessionAvailabilityResult(
                sessionId,
                false,
                false,
                pathError);
        }

        var configuredCommand =
            await settings.GetAsync(CopilotSettingKeys.Command, ct) ??
            CopilotSettingKeys.DefaultCommand;
        if (!CopilotCommandParser.TryParse(configuredCommand, out var command, out var commandError))
        {
            return new CopilotSessionAvailabilityResult(
                sessionId,
                false,
                false,
                commandError);
        }

        await _gate.WaitAsync(ct);
        try
        {
            CopilotClient? client = null;
            try
            {
                client = new CopilotClient(new CopilotClientOptions
                {
                    Connection = BuildConnection(command!),
                    BaseDirectory = copilotHome,
                    EnableRemoteSessions = false,
                    UseLoggedInUser = true,
                    LogLevel = CopilotLogLevel.Error,
                });
                await client.StartAsync(ct);
                var sessions = await client.ListSessionsAsync(null, ct);
                return new CopilotSessionAvailabilityResult(
                    sessionId,
                    true,
                    sessions.Any(item => string.Equals(
                        item.SessionId,
                        sessionId,
                        StringComparison.OrdinalIgnoreCase)),
                    null);
            }
            catch (Exception exception) when (IsExpectedSdkException(exception))
            {
                return new CopilotSessionAvailabilityResult(
                    sessionId,
                    false,
                    false,
                    exception.Message);
            }
            finally
            {
                if (client is not null)
                {
                    try
                    {
                        await client.DisposeAsync();
                    }
                    catch (Exception exception) when (IsExpectedSdkException(exception))
                    {
                        logger.LogWarning(
                            exception,
                            "Copilot SDK availability runtime did not shut down cleanly.");
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryResolveCopilotHome(out string copilotHome, out string? error)
    {
        error = null;
        var sessionStatePath = Path.GetFullPath(options.SessionStatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(
                Path.GetFileName(sessionStatePath),
                "session-state",
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            copilotHome = "";
            error =
                "Cleanup requires SessionStatePath to be the session-state directory beneath one Copilot home.";
            return false;
        }

        copilotHome = Path.GetDirectoryName(sessionStatePath) ?? "";
        if (string.IsNullOrWhiteSpace(copilotHome))
        {
            error = "Cleanup could not resolve the configured Copilot home directory.";
            return false;
        }

        var expectedDatabase = Path.GetFullPath(
            Path.Combine(copilotHome, "session-store.db"));
        var configuredDatabase = Path.GetFullPath(options.DatabasePath);
        if (!string.Equals(
                expectedDatabase,
                configuredDatabase,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            error =
                "Cleanup is disabled because DatabasePath and SessionStatePath do not belong to the same Copilot home.";
            return false;
        }

        return true;
    }

    private RuntimeConnection BuildConnection(CopilotCommandSpec command)
    {
        var executable = ResolveExecutable(command.Executable);
        var prefixArguments = command.PrefixArguments.ToList();
        var extension = Path.GetExtension(executable);
        if (OperatingSystem.IsWindows() &&
            (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)))
        {
            prefixArguments.Insert(0, executable);
            prefixArguments.Insert(0, "/c");
            prefixArguments.Insert(0, "/s");
            prefixArguments.Insert(0, "/d");
            return RuntimeConnection.ForStdio(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                prefixArguments);
        }

        if (OperatingSystem.IsWindows() &&
            string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            prefixArguments.Insert(0, executable);
            prefixArguments.Insert(0, "-File");
            prefixArguments.Insert(0, "-NoProfile");
            prefixArguments.Insert(0, "-NoLogo");
            return RuntimeConnection.ForStdio(
                powerShellHostResolver.ResolveExecutable(),
                prefixArguments);
        }

        return RuntimeConnection.ForStdio(executable, prefixArguments);
    }

    private static string ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable) ||
            executable.Contains(Path.DirectorySeparatorChar) ||
            executable.Contains(Path.AltDirectorySeparatorChar))
        {
            return Path.GetFullPath(executable);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return executable;

        var extensions = OperatingSystem.IsWindows() && string.IsNullOrEmpty(Path.GetExtension(executable))
            ? (Environment.GetEnvironmentVariable("PATHEXT") ??
               ".COM;.EXE;.BAT;.CMD;.PS1")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(
                    directory.Trim().Trim('"'),
                    executable + extension.ToLowerInvariant());
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return executable;
    }

    private static IReadOnlyList<CopilotSessionDeletionResult> FailureResults(
        IReadOnlyCollection<string> sessionIds,
        string error) =>
        sessionIds
            .Select(sessionId => new CopilotSessionDeletionResult(sessionId, false, error))
            .ToArray();

    private static bool IsExpectedSdkException(Exception exception) =>
        exception is InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            Win32Exception or
            SocketException or
            TimeoutException or
            ArgumentException or
            JsonException ||
        string.Equals(
            exception.GetType().Namespace,
            "StreamJsonRpc",
            StringComparison.Ordinal);
}
