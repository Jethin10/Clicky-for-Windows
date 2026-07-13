using System.Text.RegularExpressions;

namespace Clicky.Windows.Services;

public static partial class VoiceCommandRouter
{
    [GeneratedRegex(
        @"^\s*(?:hey\s*)?clicky[\s,.:;\-]+agent\b[\s,.:;\-]*(?<command>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AgentCommandRegex();

    public static bool TryExtractAgentCommand(string transcript, out string command)
    {
        var match = AgentCommandRegex().Match(transcript ?? string.Empty);
        command = match.Success ? match.Groups["command"].Value.Trim() : string.Empty;
        return match.Success;
    }
}
