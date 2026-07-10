using System.Text.RegularExpressions;

namespace Clicky.Windows.Services;

public static partial class AgentArtifactParser
{
    public const int MaximumFiles = 20;
    public const int MaximumCharactersPerFile = 500_000;
    public const int MaximumTotalCharacters = 2_000_000;

    [GeneratedRegex(@"\[CLICKY_FILES\]\s*(?<json>\{.*?\})\s*\[/CLICKY_FILES\]", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PackageRegex();

    public static AgentArtifactParseResult Parse(string response)
    {
        response ??= string.Empty;
        var match = PackageRegex().Match(response);
        if (!match.Success)
        {
            return new AgentArtifactParseResult(response.Trim(), null);
        }

        AgentArtifactEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<AgentArtifactEnvelope>(
                match.Groups["json"].Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The agent returned an invalid file package.", exception);
        }

        if (envelope?.Files is null || envelope.Files.Count == 0)
        {
            throw new InvalidOperationException("The agent file package did not contain any files.");
        }
        if (envelope.Files.Count > MaximumFiles)
        {
            throw new InvalidOperationException($"Agent file packages are limited to {MaximumFiles} files.");
        }

        var total = 0;
        var files = new List<AgentFileArtifact>();
        foreach (var file in envelope.Files)
        {
            var path = file.Path?.Trim();
            var content = file.Content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Every agent file needs a relative path.");
            }
            if (content.Length > MaximumCharactersPerFile)
            {
                throw new InvalidOperationException($"{path} exceeds the per-file safety limit.");
            }

            total += content.Length;
            if (total > MaximumTotalCharacters)
            {
                throw new InvalidOperationException("The agent file package exceeds the total safety limit.");
            }
            files.Add(new AgentFileArtifact(path, content));
        }

        var visibleText = response.Remove(match.Index, match.Length).Trim();
        return new AgentArtifactParseResult(
            visibleText,
            new AgentArtifactPackage(envelope.Summary?.Trim() ?? "Agent-generated files", files));
    }

    private sealed class AgentArtifactEnvelope
    {
        public string? Summary { get; set; }
        public List<AgentArtifactFileEnvelope>? Files { get; set; }
    }

    private sealed class AgentArtifactFileEnvelope
    {
        public string? Path { get; set; }
        public string? Content { get; set; }
    }
}

public sealed record AgentArtifactParseResult(string VisibleText, AgentArtifactPackage? Package);
public sealed record AgentArtifactPackage(string Summary, IReadOnlyList<AgentFileArtifact> Files);
public sealed record AgentFileArtifact(string Path, string Content);
