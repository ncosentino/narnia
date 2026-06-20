namespace NexusLabs.Narnia.Web;

/// <summary>
/// Runtime state of a launched Narnia web-server instance, persisted to a per-user file so a
/// session that did not start the server can still discover, health-check, and stop it.
/// </summary>
/// <param name="Pid">Process id of the running server.</param>
/// <param name="Port">TCP port the server is listening on.</param>
/// <param name="Url">Base URL the server is bound to (loopback).</param>
/// <param name="Version">Informational/assembly version of the running server, if known.</param>
/// <param name="ExePath">Full path to the server executable, used to verify process identity
/// before terminating a recorded PID (guards against PID reuse).</param>
/// <param name="StartedAt">UTC timestamp the server reported as started.</param>
internal sealed record WebServerRunStateInfo(
    int Pid,
    int Port,
    string Url,
    string? Version,
    string? ExePath,
    DateTimeOffset StartedAt);
