using System.Net.Http.Headers;

namespace Clicky.Windows.Services;

public sealed class UniversalModelClient : IDisposable
{
    public const string CompanionSystemPrompt = """
        you're clicky, a friendly always-on companion that lives next to the user's cursor. the user can see your response beside the cursor and may hear it aloud. answer like a warm, capable collaborator.

        rules:
        - default to one or two direct, information-dense sentences unless the user asks for depth.
        - write naturally for speech. avoid markdown unless the user explicitly asks for formatted output.
        - use specific details from the supplied screen captures whenever they are relevant.
        - never pretend you completed an action that you did not actually complete.
        - the first image is the primary display where the cursor was when the request was submitted.

        element pointing:
        append exactly one coordinate tag at the end: [POINT:x,y:label] for the first screen, [POINT:x,y:label:screenN] for another screen, or [POINT:none]. coordinates are integer pixels in the supplied image with origin at the top left.
        """;

    private readonly ProviderSettings _provider;
    private readonly HttpClient _httpClient;

    public UniversalModelClient(ProviderSettings provider, string? apiKey)
    {
        _provider = provider;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        if (string.Equals(provider.Preset, "OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", "https://github.com/Jethin10/Clicky-for-Windows");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-OpenRouter-Title", "Clicky for Windows");
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint("models"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Model discovery returned {(int)response.StatusCode}: {TrimError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<string> AnalyzeAsync(
        IReadOnlyList<CapturedScreen> screenshots,
        string transcript,
        IReadOnlyList<ConversationTurn> history,
        Action<string>? onTextChunk,
        CancellationToken cancellationToken)
    {
        if (!_provider.IsConfigured)
        {
            throw new InvalidOperationException("Configure an AI provider and model in Clicky settings.");
        }

        return _provider.Protocol == ProviderProtocol.OpenAiResponses
            ? AnalyzeWithResponsesAsync(screenshots, transcript, history, onTextChunk, cancellationToken)
            : AnalyzeWithChatCompletionsAsync(screenshots, transcript, history, onTextChunk, cancellationToken);
    }

    private async Task<string> AnalyzeWithResponsesAsync(
        IReadOnlyList<CapturedScreen> screenshots,
        string transcript,
        IReadOnlyList<ConversationTurn> history,
        Action<string>? onTextChunk,
        CancellationToken cancellationToken)
    {
        var input = new List<object>();
        foreach (var turn in history)
        {
            input.Add(new { role = "user", content = turn.UserTranscript });
            input.Add(new { role = "assistant", content = turn.AssistantResponse });
        }

        var content = new List<object>();
        for (var index = 0; _provider.SendScreenshots && index < screenshots.Count; index++)
        {
            var screenshot = screenshots[index];
            content.Add(new { type = "input_image", image_url = screenshot.DataUrl, detail = "auto" });
            content.Add(new
            {
                type = "input_text",
                text = $"{(index == 0 ? "primary focus" : $"screen {index + 1}")} dimensions: {screenshot.Width}x{screenshot.Height} pixels"
            });
        }
        content.Add(new { type = "input_text", text = transcript });
        input.Add(new { role = "user", content });

        var body = new Dictionary<string, object?>
        {
            ["model"] = _provider.Model,
            ["instructions"] = CompanionSystemPrompt,
            ["input"] = input,
            ["stream"] = true,
            ["max_output_tokens"] = 1200
        };
        if (_provider.EnableWebSearch)
        {
            body["tools"] = new[] { new { type = "web_search" } };
        }

        if (string.Equals(_provider.Preset, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            body["safety_identifier"] = _provider.SafetyIdentifier;
        }

        return await SendStreamingAsync("responses", body, ParseResponsesDelta, onTextChunk, cancellationToken);
    }

    private async Task<string> AnalyzeWithChatCompletionsAsync(
        IReadOnlyList<CapturedScreen> screenshots,
        string transcript,
        IReadOnlyList<ConversationTurn> history,
        Action<string>? onTextChunk,
        CancellationToken cancellationToken)
    {
        var messages = new List<object> { new { role = "system", content = CompanionSystemPrompt } };
        foreach (var turn in history)
        {
            messages.Add(new { role = "user", content = turn.UserTranscript });
            messages.Add(new { role = "assistant", content = turn.AssistantResponse });
        }

        var content = new List<object>();
        for (var index = 0; _provider.SendScreenshots && index < screenshots.Count; index++)
        {
            var screenshot = screenshots[index];
            content.Add(new { type = "image_url", image_url = new { url = screenshot.DataUrl, detail = "auto" } });
            content.Add(new
            {
                type = "text",
                text = $"{(index == 0 ? "primary focus" : $"screen {index + 1}")} dimensions: {screenshot.Width}x{screenshot.Height} pixels"
            });
        }
        content.Add(new { type = "text", text = transcript });
        messages.Add(new { role = "user", content });

        var body = new Dictionary<string, object?>
        {
            ["model"] = _provider.Model,
            ["messages"] = messages,
            ["stream"] = true
        };

        body[string.Equals(_provider.Preset, "MiMo", StringComparison.OrdinalIgnoreCase)
            ? "max_completion_tokens"
            : "max_tokens"] = 1200;

        if (_provider.EnableWebSearch && string.Equals(_provider.Preset, "OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            body["plugins"] = new[] { new { id = "web" } };
        }
        else if (_provider.EnableWebSearch && string.Equals(_provider.Preset, "MiMo", StringComparison.OrdinalIgnoreCase))
        {
            body["tools"] = new[] { new { type = "web_search", max_keyword = 3 } };
        }

        return await SendStreamingAsync("chat/completions", body, ParseChatDelta, onTextChunk, cancellationToken);
    }

    private async Task<string> SendStreamingAsync(
        string relativePath,
        object body,
        Func<JsonElement, string?> parseDelta,
        Action<string>? onTextChunk,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(relativePath));
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{_provider.DisplayName} returned {(int)response.StatusCode}: {TrimError(error)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = new StringBuilder();
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (payload == "[DONE]")
            {
                break;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                var chunk = parseDelta(document.RootElement);
                if (string.IsNullOrEmpty(chunk))
                {
                    continue;
                }

                result.Append(chunk);
                onTextChunk?.Invoke(result.ToString());
            }
            catch (JsonException)
            {
                // Ignore malformed keep-alive or provider-specific events.
            }
        }

        return result.ToString().Trim();
    }

    private static string? ParseResponsesDelta(JsonElement root)
    {
        return root.TryGetProperty("type", out var type)
            && type.GetString() == "response.output_text.delta"
            && root.TryGetProperty("delta", out var delta)
                ? delta.GetString()
                : null;
    }

    private static string? ParseChatDelta(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var first = choices[0];
        return first.TryGetProperty("delta", out var delta)
            && delta.TryGetProperty("content", out var content)
                ? content.GetString()
                : null;
    }

    private Uri Endpoint(string relativePath)
    {
        var normalizedBase = _provider.BaseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBase, UriKind.Absolute), relativePath.TrimStart('/'));
    }

    private static string TrimError(string text) => text.Length <= 500 ? text : text[..500] + "...";

    public void Dispose() => _httpClient.Dispose();
}
