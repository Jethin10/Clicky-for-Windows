using MimeKit;
using System.Text.RegularExpressions;

namespace Clicky.Windows.Services;

public static partial class AgentEmailParser
{
    public const int MaximumRecipients = 10;
    public const int MaximumSubjectCharacters = 200;
    public const int MaximumBodyCharacters = 100_000;

    [GeneratedRegex(@"\[CLICKY_EMAIL\]\s*(?<json>\{.*?\})\s*\[/CLICKY_EMAIL\]", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    public static AgentEmailParseResult Parse(string response)
    {
        response ??= string.Empty;
        var match = EmailRegex().Match(response);
        if (!match.Success)
        {
            return new AgentEmailParseResult(response.Trim(), null);
        }

        AgentEmailEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<AgentEmailEnvelope>(
                match.Groups["json"].Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The agent returned an invalid email proposal.", exception);
        }

        var to = ValidateRecipients(envelope?.To, "To");
        var cc = ValidateRecipients(envelope?.Cc, "Cc");
        if (to.Count == 0)
        {
            throw new InvalidOperationException("An email proposal needs at least one To recipient.");
        }
        if (to.Count + cc.Count > MaximumRecipients)
        {
            throw new InvalidOperationException($"Email proposals are limited to {MaximumRecipients} recipients.");
        }

        var subject = envelope?.Subject?.Trim() ?? string.Empty;
        var body = envelope?.Body?.Trim() ?? string.Empty;
        if (subject.Length == 0 || subject.Length > MaximumSubjectCharacters)
        {
            throw new InvalidOperationException($"Email subjects must be between 1 and {MaximumSubjectCharacters} characters.");
        }
        if (body.Length == 0 || body.Length > MaximumBodyCharacters)
        {
            throw new InvalidOperationException($"Email bodies must be between 1 and {MaximumBodyCharacters:N0} characters.");
        }

        return new AgentEmailParseResult(
            response.Remove(match.Index, match.Length).Trim(),
            new AgentEmailProposal(to, cc, subject, body));
    }

    private static IReadOnlyList<string> ValidateRecipients(IReadOnlyList<string>? recipients, string field)
    {
        if (recipients is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var recipient in recipients)
        {
            if (!MailboxAddress.TryParse(recipient, out var mailbox))
            {
                throw new InvalidOperationException($"{field} contains an invalid email address: {recipient}");
            }
            result.Add(mailbox.Address);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class AgentEmailEnvelope
    {
        public List<string>? To { get; set; }
        public List<string>? Cc { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
    }
}

public sealed record AgentEmailParseResult(string VisibleText, AgentEmailProposal? Proposal);
public sealed record AgentEmailProposal(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    string Body);
