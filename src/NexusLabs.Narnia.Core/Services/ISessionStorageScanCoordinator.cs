namespace NexusLabs.Narnia.Core.Services;

/// <summary>Coordinates background session-storage scans for the web host.</summary>
public interface ISessionStorageScanCoordinator
{
    /// <summary>Gets current scanner progress.</summary>
    /// <returns>Current in-memory status and progress.</returns>
    SessionStorageScanProgress GetProgress();

    /// <summary>Requests a scan when one is not already running or queued.</summary>
    /// <returns><c>true</c> when the request was accepted; otherwise <c>false</c>.</returns>
    bool RequestScan();
}
