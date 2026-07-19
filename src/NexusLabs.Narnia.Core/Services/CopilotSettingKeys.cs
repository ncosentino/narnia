namespace NexusLabs.Narnia.Core.Services;

/// <summary>Settings keys and defaults shared by every Narnia Copilot invocation.</summary>
public static class CopilotSettingKeys
{
    /// <summary>Gets the settings key containing the command used to invoke Copilot.</summary>
    public const string Command = "copilot_command";

    /// <summary>Gets the default Copilot command.</summary>
    public const string DefaultCommand = "copilot";
}
