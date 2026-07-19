using System.Diagnostics;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Finds live processes whose executable image is named <c>copilot</c>.</summary>
public sealed class CopilotProcessProvider : ICopilotProcessProvider
{
    /// <inheritdoc />
    public IReadOnlyList<int> GetProcessIds()
    {
        var processes = Process.GetProcessesByName("copilot");
        try
        {
            return processes
                .Select(process => process.Id)
                .Order()
                .ToArray();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
