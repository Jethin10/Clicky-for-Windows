namespace Clicky.Windows.Services;

public static class AgentResultService
{
    public const int MaximumResultCharacters = 500_000;
    public const int MaximumTaskCharacters = 500;
    public const int MaximumPreviewCharacters = 120;

    public static AgentTaskResult Create(
        string prompt,
        string providerName,
        string result,
        DateTimeOffset completedAt)
    {
        var task = (prompt ?? string.Empty)
            .Split("\n\nattached local document:", 2, StringSplitOptions.None)[0]
            .Trim();
        if (task.Length > MaximumTaskCharacters)
        {
            task = task[..(MaximumTaskCharacters - 3)] + "...";
        }

        result = (result ?? string.Empty).Trim();
        if (result.Length > MaximumResultCharacters)
        {
            result = result[..MaximumResultCharacters]
                + "\n\n[Result truncated at Clicky's 500,000-character safety limit.]";
        }
        return new AgentTaskResult(task, providerName.Trim(), result, completedAt);
    }

    public static string Preview(string result)
    {
        var preview = (result ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return preview.Length > MaximumPreviewCharacters
            ? preview[..(MaximumPreviewCharacters - 3)] + "..."
            : preview;
    }
}

public sealed record AgentTaskResult(
    string Task,
    string ProviderName,
    string Text,
    DateTimeOffset CompletedAt);
