using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class CopilotCommandParserTests
{
    [Theory]
    [InlineData("copilot", "copilot", new string[0])]
    [InlineData("agency copilot", "agency", new[] { "copilot" })]
    [InlineData("\"C:\\Program Files\\Agency\\agency.exe\" copilot", "C:\\Program Files\\Agency\\agency.exe", new[] { "copilot" })]
    public void TryParse_ValidCommands_ReturnExecutableAndPrefixArguments(
        string command,
        string expectedExecutable,
        string[] expectedArguments)
    {
        var parsed = CopilotCommandParser.TryParse(command, out var spec, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(expectedExecutable, spec!.Executable);
        Assert.Equal(expectedArguments, spec.PrefixArguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"C:\\Program Files\\copilot.exe")]
    public void TryParse_InvalidCommands_ReturnError(string command)
    {
        var parsed = CopilotCommandParser.TryParse(command, out var spec, out var error);

        Assert.False(parsed);
        Assert.Null(spec);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
