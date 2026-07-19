using System.Text;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Executable and leading arguments parsed from Narnia's Copilot command setting.</summary>
/// <param name="Executable">Executable name or path.</param>
/// <param name="PrefixArguments">Arguments placed before SDK runtime arguments.</param>
public sealed record CopilotCommandSpec(
    string Executable,
    IReadOnlyList<string> PrefixArguments);

/// <summary>Parses a configured Copilot command without invoking a shell.</summary>
public static class CopilotCommandParser
{
    /// <summary>Parses an executable plus quoted leading arguments.</summary>
    /// <param name="command">Configured command such as <c>copilot</c> or <c>agency copilot</c>.</param>
    /// <param name="spec">Parsed executable and leading arguments when successful.</param>
    /// <param name="error">Validation error when parsing fails.</param>
    /// <returns><c>true</c> when the command is valid.</returns>
    public static bool TryParse(
        string command,
        out CopilotCommandSpec? spec,
        out string? error)
    {
        spec = null;
        error = null;
        if (string.IsNullOrWhiteSpace(command))
        {
            error = "The configured Copilot command is blank.";
            return false;
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                    continue;
                }

                if (character == '\\' &&
                    quote == '"' &&
                    index + 1 < command.Length &&
                    command[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                current.Append(character);
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                AddToken(tokens, current);
                continue;
            }

            current.Append(character);
        }

        if (quote is not null)
        {
            error = "The configured Copilot command contains an unmatched quote.";
            return false;
        }

        AddToken(tokens, current);
        if (tokens.Count == 0)
        {
            error = "The configured Copilot command does not contain an executable.";
            return false;
        }

        spec = new CopilotCommandSpec(tokens[0], tokens.Skip(1).ToArray());
        return true;
    }

    private static void AddToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
            return;
        tokens.Add(current.ToString());
        current.Clear();
    }
}
