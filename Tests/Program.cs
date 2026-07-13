using Clicky.Windows.Services;
using System.Net;
using System.Net.Sockets;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using Drawing = System.Drawing;

var failures = new List<string>();

Check(OnboardingMediaService.ManifestUri.Scheme == Uri.UriSchemeHttps, "onboarding media uses HTTPS");
Check(OnboardingMediaService.PlayerUri.Host == "player.mux.com", "onboarding uses official Mux player");
Check(OnboardingMediaService.PlayerUri.Query.Contains("disable-tracking=true", StringComparison.Ordinal), "onboarding disables Mux tracking");
Check(OnboardingMediaService.PlayerUri.Query.Contains("disable-cookies=true", StringComparison.Ordinal), "onboarding disables Mux cookies");
var onboardingEmbed = OnboardingMediaService.BuildEmbedHtml();
Check(onboardingEmbed.Contains("<iframe", StringComparison.OrdinalIgnoreCase)
    && onboardingEmbed.Contains(OnboardingMediaService.PlaybackId, StringComparison.Ordinal), "onboarding iframe embed composition");
var onboardingPosition = OnboardingMediaService.PositionNearCursor(
    new Drawing.Point(1_900, 1_050),
    new Drawing.Rectangle(0, 0, 1_920, 1_080),
    370,
    252);
Check(onboardingPosition.Left < 1_530 && onboardingPosition.Top < 798, "onboarding flips left and above near screen edge");
var secondaryPosition = OnboardingMediaService.PositionNearCursor(
    new Drawing.Point(-1_900, 100),
    new Drawing.Rectangle(-1_920, 0, 1_920, 1_080),
    370,
    252);
Check(secondaryPosition.Left >= -1_920 && secondaryPosition.Top >= 0, "onboarding clamps within negative-coordinate monitor");

Check(VoiceCommandRouter.TryExtractAgentCommand(
    "HeyClicky agent, find cameras under one thousand dollars",
    out var compactCommand)
    && compactCommand == "find cameras under one thousand dollars",
    "compact HeyClicky agent phrase");

Check(VoiceCommandRouter.TryExtractAgentCommand(
    "hey clicky, agent: turn this into a webpage",
    out var spacedCommand)
    && spacedCommand == "turn this into a webpage",
    "spaced Hey Clicky agent phrase");

Check(!VoiceCommandRouter.TryExtractAgentCommand(
    "clicky, explain this panel",
    out _),
    "ordinary Clicky question remains interactive");

var pcm = new byte[3_200];
var wav = PcmWaveEncoder.Encode16KhzMono(pcm);
Check(wav.Length > pcm.Length, "WAV contains a header");
Check(Encoding.ASCII.GetString(wav, 0, 4) == "RIFF", "WAV RIFF signature");
Check(Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", "WAV format signature");
Check(BitConverter.ToInt32(wav, 24) == 16_000, "WAV sample rate");
Check(BitConverter.ToInt16(wav, 22) == 1, "WAV mono channel count");

var provider = ProviderSettings.FromPreset("OpenAI");
var audio = new AudioSettings();
Check(!new ClickySettings().OnboardingCompleted, "onboarding requires one explicit first-run continuation");
var onboardingSettingsJson = JsonSerializer.Serialize(new ClickySettings { OnboardingCompleted = true });
Check(JsonSerializer.Deserialize<ClickySettings>(onboardingSettingsJson)?.OnboardingCompleted == true, "onboarding completion persistence");
Check(audio.IsConfigured(provider), "default direct-audio settings");
Check(audio.EffectiveBaseUrl(provider) == provider.BaseUrl, "audio inherits provider base URL");
Check(audio.EnableWindowsSpeechFallback, "Windows speech fallback enabled by default");

var localRecognizers = WindowsSpeechRecognitionService.InstalledRecognizers();
var localVoices = WindowsSpeechSynthesisService.InstalledVoices();
Check(localVoices.Count > 0, "Windows local speech voice discovery");
if (localRecognizers.Count > 0)
{
    using var speechWave = new MemoryStream();
    using (var synthesizer = new SpeechSynthesizer())
    {
        synthesizer.SetOutputToWaveStream(speechWave);
        synthesizer.Speak("Clicky local speech verification");
    }
    Check(speechWave.Length > 44, "Windows local speech synthesis WAV output");
    var localTranscript = await new WindowsSpeechRecognitionService().RecognizeWaveAsync(
        speechWave.ToArray(),
        localRecognizers[0].Culture,
        CancellationToken.None);
    Console.WriteLine($"Windows offline speech transcript ({localRecognizers[0].Culture}): {localTranscript}");
    Check(localTranscript.Contains("local speech verification", StringComparison.OrdinalIgnoreCase), "Windows offline speech recognition");

    using var rawPcm = new MemoryStream();
    using (var synthesizer = new SpeechSynthesizer())
    {
        synthesizer.SetOutputToAudioStream(
            rawPcm,
            new SpeechAudioFormatInfo(16_000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        synthesizer.Speak("Windows PCM fallback verification");
    }
    var pcmTranscript = await new WindowsSpeechRecognitionService().RecognizePcm16Async(
        rawPcm.ToArray(),
        localRecognizers[0].Culture,
        CancellationToken.None);
    Check(pcmTranscript.Contains("fallback verification", StringComparison.OrdinalIgnoreCase), "recorded PCM Windows speech fallback");
}
else
{
    Console.WriteLine("Windows offline speech recognition test skipped: no recognizer language is installed.");
}

var fallbackInvoked = false;
var fallbackTranscript = await TranscriptionFallbackPolicy.ExecuteAsync(
    _ => Task.FromException<string>(new HttpRequestException("offline probe")),
    _ =>
    {
        fallbackInvoked = true;
        return Task.FromResult("local fallback transcript");
    },
    fallbackEnabled: true,
    CancellationToken.None);
Check(fallbackInvoked && fallbackTranscript == "local fallback transcript", "failed cloud transcription uses local fallback");

fallbackInvoked = false;
var primaryTranscript = await TranscriptionFallbackPolicy.ExecuteAsync(
    _ => Task.FromResult("cloud transcript"),
    _ =>
    {
        fallbackInvoked = true;
        return Task.FromResult("unexpected fallback");
    },
    fallbackEnabled: true,
    CancellationToken.None);
Check(!fallbackInvoked && primaryTranscript == "cloud transcript", "successful cloud transcription remains primary");

var transcriptionProbe = await ProbeTranscriptionEndpointAsync();
Check(transcriptionProbe.Transcript == "voice agent test", "direct transcription response parsing");
Check(transcriptionProbe.SawExpectedPath, "direct transcription endpoint path");
Check(transcriptionProbe.SawAuthorization, "direct transcription bearer authentication");
Check(transcriptionProbe.SawWavePayload, "direct transcription WAV upload");

var assemblyTokenProbe = await ProbeAssemblyTokenAsync();
Check(assemblyTokenProbe.Token == "short lived/token+value", "AssemblyAI Worker token parsing");
Check(assemblyTokenProbe.Request.Path == "/transcribe-token", "AssemblyAI Worker token endpoint path");
var assemblySocketUri = AssemblyAiTranscriptionSession.BuildWebSocketUri(assemblyTokenProbe.Token);
Check(assemblySocketUri.Scheme == "wss" && assemblySocketUri.Host == "streaming.assemblyai.com"
    && assemblySocketUri.AbsolutePath == "/v3/ws", "AssemblyAI secure streaming endpoint");
Check(assemblySocketUri.Query.Contains("sample_rate=16000", StringComparison.Ordinal)
    && assemblySocketUri.Query.Contains("encoding=pcm_s16le", StringComparison.Ordinal)
    && assemblySocketUri.Query.Contains("format_turns=true", StringComparison.Ordinal)
    && assemblySocketUri.Query.Contains("speech_model=u3-rt-pro", StringComparison.Ordinal)
    && assemblySocketUri.Query.Contains("token=short%20lived%2Ftoken%2Bvalue", StringComparison.Ordinal), "AssemblyAI streaming query and escaped token");
var assemblyBegin = AssemblyAiTranscriptionSession.ParseServerMessage("{\"type\":\"Begin\"}");
var assemblyPartial = AssemblyAiTranscriptionSession.ParseServerMessage("{\"type\":\"Turn\",\"transcript\":\"  partial words  \",\"end_of_turn\":false}");
var assemblyFinal = AssemblyAiTranscriptionSession.ParseServerMessage("{\"type\":\"Turn\",\"transcript\":\"final words\",\"turn_is_formatted\":true}");
var assemblyError = AssemblyAiTranscriptionSession.ParseServerMessage("{\"type\":\"Error\",\"error\":\"bad token\"}");
Check(assemblyBegin.Type == "begin", "AssemblyAI Begin event parsing");
Check(assemblyPartial.Transcript == "partial words" && !assemblyPartial.IsFinal, "AssemblyAI partial turn parsing");
Check(assemblyFinal.Transcript == "final words" && assemblyFinal.IsFinal, "AssemblyAI final formatted turn parsing");
Check(assemblyError.Type == "error" && assemblyError.Error == "bad token", "AssemblyAI error event parsing");

var responsesProbe = await ProbeUniversalModelAsync("OpenAI", ProviderProtocol.OpenAiResponses);
Check(responsesProbe.Result == "hello clicky", "OpenAI Responses SSE accumulation");
Check(responsesProbe.CallbacksValid, "OpenAI Responses cumulative streaming callbacks");
Check(responsesProbe.Path == "/v1/responses", "OpenAI Responses endpoint path");
Check(responsesProbe.Authorization == "Bearer model-key", "OpenAI Responses bearer authentication");
Check(responsesProbe.Body.Contains("\"type\":\"input_image\"", StringComparison.Ordinal)
    && responsesProbe.Body.Contains("\"type\":\"web_search\"", StringComparison.Ordinal)
    && responsesProbe.Body.Contains("prior question", StringComparison.Ordinal), "OpenAI Responses multimodal history and web-search payload");

var compatibleProbe = await ProbeUniversalModelAsync("Custom", ProviderProtocol.OpenAiChatCompletions);
Check(compatibleProbe.Result == "hello clicky", "compatible chat SSE accumulation");
Check(compatibleProbe.CallbacksValid, "compatible chat cumulative streaming callbacks");
Check(compatibleProbe.Path == "/v1/chat/completions", "compatible chat endpoint path");
Check(compatibleProbe.Body.Contains("\"type\":\"image_url\"", StringComparison.Ordinal)
    && compatibleProbe.Body.Contains("\"max_tokens\":1200", StringComparison.Ordinal), "compatible chat multimodal payload");

var openRouterProbe = await ProbeUniversalModelAsync("OpenRouter", ProviderProtocol.OpenAiChatCompletions);
Check(openRouterProbe.CallbacksValid, "OpenRouter cumulative streaming callbacks");
Check(openRouterProbe.Body.Contains("\"plugins\":[{\"id\":\"web\"}]", StringComparison.Ordinal), "OpenRouter web plugin payload");
Check(openRouterProbe.Headers.Contains("X-OpenRouter-Title: Clicky for Windows", StringComparison.OrdinalIgnoreCase)
    && openRouterProbe.Headers.Contains("HTTP-Referer: https://github.com/Jethin10/Clicky-for-Windows", StringComparison.OrdinalIgnoreCase), "OpenRouter attribution headers");

var mimoProbe = await ProbeUniversalModelAsync("MiMo", ProviderProtocol.OpenAiChatCompletions);
Check(mimoProbe.CallbacksValid, "MiMo cumulative streaming callbacks");
Check(mimoProbe.Body.Contains("\"max_completion_tokens\":1200", StringComparison.Ordinal)
    && mimoProbe.Body.Contains("\"type\":\"web_search\"", StringComparison.Ordinal)
    && mimoProbe.Body.Contains("\"max_keyword\":3", StringComparison.Ordinal), "MiMo token and web-search payload");

var streamingError = await ProbeUniversalModelErrorAsync();
Check(streamingError.Contains("quota exhausted", StringComparison.OrdinalIgnoreCase), "provider SSE error propagation");

var workerProbe = await ProbeWorkerModelAsync();
Check(workerProbe.Result == "worker reply", "Worker Claude SSE accumulation");
Check(workerProbe.Request.Path == "/chat", "Worker Claude endpoint path");
Check(workerProbe.Request.Body.Contains("\"type\":\"image\"", StringComparison.Ordinal)
    && workerProbe.Request.Body.Contains("\"media_type\":\"image/jpeg\"", StringComparison.Ordinal)
    && workerProbe.Request.Body.Contains("prior question", StringComparison.Ordinal), "Worker Claude multimodal history payload");

var directSpeechProbe = await ProbeDirectSpeechAsync();
Check(directSpeechProbe.Audio.SequenceEqual("fake-mp3"u8.ToArray()), "direct speech audio response");
Check(directSpeechProbe.Request.Path == "/v1/audio/speech"
    && directSpeechProbe.Request.Authorization == "Bearer speech-key", "direct speech endpoint and authentication");
Check(directSpeechProbe.Request.Body.Contains("\"model\":\"tts-1-hd\"", StringComparison.Ordinal)
    && directSpeechProbe.Request.Body.Contains("\"voice\":\"nova\"", StringComparison.Ordinal), "direct speech model and voice payload");

var workerSpeechProbe = await ProbeWorkerSpeechAsync();
Check(workerSpeechProbe.Audio.SequenceEqual("worker-mp3"u8.ToArray()), "Worker TTS audio response");
Check(workerSpeechProbe.Request.Path == "/tts"
    && workerSpeechProbe.Request.Body.Contains("\"model_id\":\"eleven_flash_v2_5\"", StringComparison.Ordinal)
    && workerSpeechProbe.Request.Body.Contains("\"similarity_boost\":0.75", StringComparison.Ordinal), "Worker TTS endpoint and voice payload");

var point = PointerTagParser.Parse("right here [POINT:320,240:button:screen2]");
Check(point.SpokenText == "right here", "pointer spoken text");
Check(point.Pixel == new Drawing.Point(320, 240), "pointer coordinates");
Check(point.ScreenNumber == 2, "pointer screen number");
var edgeCapture = new CapturedScreen(new Drawing.Rectangle(100, 200, 1_920, 1_080), 1_280, 720, string.Empty);
var edgePoint = PointerTagParser.MapToScreen(
    PointerTagParser.Parse("edge [POINT:1280,720:corner]"),
    [edgeCapture]);
Check(edgePoint is { X: < 2020, Y: < 1280 }, "pointer mapping stays inside the overlay at screenshot edges");

var documentService = new DocumentContextService();
var textFixture = Path.Combine(Path.GetTempPath(), $"clicky-document-{Guid.NewGuid():N}.md");
try
{
    await File.WriteAllTextAsync(textFixture, "Clicky attachment smoke test.\nSecond line.");
    var textContext = await documentService.ExtractAsync(textFixture, CancellationToken.None);
    Check(textContext.Text.Contains("attachment smoke test", StringComparison.Ordinal), "text attachment extraction");
    Check(DocumentContextService.AddToPrompt("summarize it", textContext).Contains("begin attached document", StringComparison.Ordinal), "document prompt composition");
}
finally
{
    File.Delete(textFixture);
}

var imageFixture = Path.Combine(Path.GetTempPath(), $"clicky-ocr-{Guid.NewGuid():N}.png");
try
{
    using var bitmap = new Drawing.Bitmap(1_600, 420);
    using var graphics = Drawing.Graphics.FromImage(bitmap);
    graphics.Clear(Drawing.Color.White);
    using var font = new Drawing.Font("Arial", 64, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
    graphics.DrawString("Clicky local OCR verification 2026", font, Drawing.Brushes.Black, new Drawing.PointF(45, 120));
    bitmap.Save(imageFixture, Drawing.Imaging.ImageFormat.Png);
    var imageContext = await documentService.ExtractAsync(imageFixture, CancellationToken.None);
    Check(imageContext.Text.Contains("Clicky local OCR verification", StringComparison.OrdinalIgnoreCase), "local image OCR extraction");
    Check(imageContext.UsedLocalOcr, "local image OCR provenance");
}
finally
{
    File.Delete(imageFixture);
}

var pdfFixture = Environment.GetEnvironmentVariable("CLICKY_TEST_PDF");
if (!string.IsNullOrWhiteSpace(pdfFixture) && File.Exists(pdfFixture))
{
    var pdfContext = await documentService.ExtractAsync(pdfFixture, CancellationToken.None);
    Check(pdfContext.PageCount == 2, "PDF page count");
    Check(pdfContext.Text.Contains("Clicky PDF verification", StringComparison.Ordinal), "PDF text extraction");
    Check(pdfContext.Text.Contains("second page confirms extraction order", StringComparison.OrdinalIgnoreCase), "PDF multi-page extraction");
}


var scannedPdfFixture = Environment.GetEnvironmentVariable("CLICKY_TEST_SCANNED_PDF");
if (!string.IsNullOrWhiteSpace(scannedPdfFixture) && File.Exists(scannedPdfFixture))
{
    var scannedContext = await documentService.ExtractAsync(scannedPdfFixture, CancellationToken.None);
    Check(scannedContext.Text.Contains("Scanned Clicky verification", StringComparison.OrdinalIgnoreCase), "scanned PDF local OCR extraction");
    Check(scannedContext.Text.Contains("local OCR", StringComparison.OrdinalIgnoreCase), "scanned PDF OCR provenance marker");
    Check(scannedContext.UsedLocalOcr, "scanned PDF OCR provenance state");
}

var mixedPdfFixture = Environment.GetEnvironmentVariable("CLICKY_TEST_MIXED_PDF");
if (!string.IsNullOrWhiteSpace(mixedPdfFixture) && File.Exists(mixedPdfFixture))
{
    var mixedContext = await documentService.ExtractAsync(mixedPdfFixture, CancellationToken.None);
    var selectableIndex = mixedContext.Text.IndexOf("Selectable first page", StringComparison.OrdinalIgnoreCase);
    var scannedIndex = mixedContext.Text.IndexOf("Scanned second page", StringComparison.OrdinalIgnoreCase);
    Check(selectableIndex >= 0 && scannedIndex > selectableIndex, "mixed PDF original page order");
    Check(mixedContext.Text.Contains("[page 2, local OCR]", StringComparison.OrdinalIgnoreCase), "mixed PDF OCR page marker");
}

var artifactResponse = """
    built the page. [POINT:none]
    [CLICKY_FILES]
    {"summary":"A small webpage","files":[{"path":"index.html","content":"<h1>Hello Clicky</h1>"},{"path":"styles/site.css","content":"h1 { color: blue; }"}]}
    [/CLICKY_FILES]
    """;
var artifact = AgentArtifactParser.Parse(artifactResponse);
Check(artifact.Package?.Files.Count == 2, "agent artifact package parsing");
Check(artifact.VisibleText.Contains("built the page", StringComparison.Ordinal), "agent artifact visible response");

var researchResultText = "Camera comparison\n\n1. First option\n2. Second option\n\nRecommendation: first option.";
var storedAgentResult = AgentResultService.Create(
    "find cameras under $1,000\n\nattached local document: reference.pdf\ncontent",
    "OpenRouter",
    researchResultText,
    DateTimeOffset.Parse("2026-07-11T12:00:00Z"));
Check(storedAgentResult.Task == "find cameras under $1,000", "agent result task strips attached document payload");
Check(storedAgentResult.Text == researchResultText, "complete agent research result preserved");
Check(!AgentResultService.Preview(researchResultText).Contains('\n'), "agent result single-line preview");
var boundedAgentResult = AgentResultService.Create(
    "large result",
    "test provider",
    new string('x', AgentResultService.MaximumResultCharacters + 1),
    DateTimeOffset.UtcNow);
Check(boundedAgentResult.Text.Contains("Result truncated", StringComparison.Ordinal), "agent result safety bound");

var emailResponse = """
    Draft ready.
    [CLICKY_EMAIL]
    {"to":["alex@example.com"],"cc":["team@example.com"],"subject":"Clicky update","body":"The Windows build is ready."}
    [/CLICKY_EMAIL]
    """;
var emailProposal = AgentEmailParser.Parse(emailResponse);
Check(emailProposal.Proposal?.To.Single() == "alex@example.com", "agent email recipient parsing");
Check(emailProposal.Proposal?.Subject == "Clicky update", "agent email subject parsing");
var invalidRecipientRejected = false;
try
{
    _ = AgentEmailParser.Parse("[CLICKY_EMAIL]{\"to\":[\"not an address\"],\"subject\":\"Test\",\"body\":\"Hello\"}[/CLICKY_EMAIL]");
}
catch (InvalidOperationException)
{
    invalidRecipientRejected = true;
}
Check(invalidRecipientRejected, "agent invalid email recipient rejection");

var smtpProbe = await ProbeSmtpDeliveryAsync(emailProposal.Proposal!);
Check(smtpProbe.Contains("alex@example.com", StringComparison.OrdinalIgnoreCase), "SMTP recipient delivery");
Check(smtpProbe.Contains("Subject: Clicky update", StringComparison.OrdinalIgnoreCase), "SMTP subject delivery");
Check(smtpProbe.Contains("The Windows build is ready.", StringComparison.Ordinal), "SMTP body delivery");

var workspaceService = new AgentWorkspaceService();
var agentWorkspace = Path.Combine(Path.GetTempPath(), $"clicky-agent-{Guid.NewGuid():N}");
Directory.CreateDirectory(agentWorkspace);
try
{
    var package = artifact.Package!;
    var plans = workspaceService.Plan(agentWorkspace, package);
    Check(plans.Count == 2 && plans.All(plan => !plan.WillOverwrite), "agent create plan");
    var writeResult = await workspaceService.ApplyAsync(agentWorkspace, plans, CancellationToken.None);
    Check(File.ReadAllText(Path.Combine(agentWorkspace, "index.html")) == "<h1>Hello Clicky</h1>", "agent atomic file write");

    var overwritePackage = new AgentArtifactPackage(
        "Update page",
        [new AgentFileArtifact("index.html", "<h1>Updated</h1>")]);
    var overwritePlans = workspaceService.Plan(agentWorkspace, overwritePackage);
    Check(overwritePlans.Single().WillOverwrite, "agent overwrite plan");
    var overwriteResult = await workspaceService.ApplyAsync(agentWorkspace, overwritePlans, CancellationToken.None);
    Check(File.ReadAllText(Path.Combine(agentWorkspace, "index.html")) == "<h1>Updated</h1>", "agent approved overwrite");
    Check(File.Exists(Path.Combine(overwriteResult.BackupPath, "index.html")), "agent overwrite backup");
    if (Directory.Exists(overwriteResult.BackupPath))
    {
        Directory.Delete(overwriteResult.BackupPath, recursive: true);
    }

    var traversalRejected = false;
    try
    {
        _ = workspaceService.Plan(
            agentWorkspace,
            new AgentArtifactPackage("escape", [new AgentFileArtifact("../escape.txt", "blocked")]));
    }
    catch (InvalidOperationException)
    {
        traversalRejected = true;
    }
    Check(traversalRejected, "agent traversal rejection");
}
finally
{
    if (Directory.Exists(agentWorkspace))
    {
        Directory.Delete(agentWorkspace, recursive: true);
    }
}

if (failures.Count == 0)
{
    Console.WriteLine("Clicky smoke tests passed.");
    return 0;
}

Console.Error.WriteLine($"Clicky smoke tests failed ({failures.Count}):");
foreach (var failure in failures)
{
    Console.Error.WriteLine($"- {failure}");
}
return 1;

void Check(bool condition, string name)
{
    if (!condition)
    {
        failures.Add(name);
    }
}

static async Task<TranscriptionProbe> ProbeTranscriptionEndpointAsync()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var observation = new TaskCompletionSource<(bool Path, bool Auth, bool Wave)>(TaskCreationOptions.RunContinuationsAsynchronously);

    var server = Task.Run(async () =>
    {
        using var socket = await listener.AcceptTcpClientAsync();
        await using var stream = socket.GetStream();
        using var received = new MemoryStream();
        var buffer = new byte[8_192];
        var headerEnd = -1;
        var contentLength = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
            {
                break;
            }

            received.Write(buffer, 0, count);
            var bytes = received.ToArray();
            if (headerEnd < 0)
            {
                headerEnd = FindSequence(bytes, "\r\n\r\n"u8.ToArray());
                if (headerEnd >= 0)
                {
                    var headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);
                    var contentLengthLine = headerText.Split("\r\n")
                        .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    _ = int.TryParse(contentLengthLine?.Split(':', 2)[1].Trim(), out contentLength);
                }
            }

            if (headerEnd >= 0 && bytes.Length >= headerEnd + 4 + contentLength)
            {
                var headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);
                var bodyText = Encoding.Latin1.GetString(bytes, headerEnd + 4, contentLength);
                observation.SetResult((
                    headerText.StartsWith("POST /v1/audio/transcriptions ", StringComparison.Ordinal),
                    headerText.Contains("Authorization: Bearer smoke-key", StringComparison.OrdinalIgnoreCase),
                    bodyText.Contains("RIFF", StringComparison.Ordinal) && bodyText.Contains("gpt-4o-mini-transcribe", StringComparison.Ordinal)));
                break;
            }
        }

        const string json = "{\"text\":\"voice agent test\"}";
        var payload = Encoding.UTF8.GetBytes(json);
        var responseHeader = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHeader);
        await stream.WriteAsync(payload);
    });

    try
    {
        var audioSettings = new AudioSettings
        {
            BaseUrl = $"http://127.0.0.1:{port}/v1",
            TranscriptionModel = "gpt-4o-mini-transcribe"
        };
        using var client = new OpenAiAudioClient(audioSettings, ProviderSettings.FromPreset("OpenAI"), "smoke-key");
        var transcript = await client.TranscribeAsync(new byte[3_200], CancellationToken.None);
        var observed = await observation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.WaitAsync(TimeSpan.FromSeconds(5));
        return new TranscriptionProbe(transcript, observed.Path, observed.Auth, observed.Wave);
    }
    finally
    {
        listener.Stop();
    }
}

static async Task<HttpProbe> ProbeUniversalModelAsync(string preset, ProviderProtocol protocol)
{
    var sse = protocol == ProviderProtocol.OpenAiResponses
        ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"hello \"}\n\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"clicky\"}\n\ndata: [DONE]\n\n"
        : "data: {\"choices\":[{\"delta\":{\"content\":\"hello \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"clicky\"}}]}\n\ndata: [DONE]\n\n";
    var server = StartHttpProbe(sse, "text/event-stream");
    try
    {
        var provider = ProviderSettings.FromPreset(preset);
        provider.BaseUrl = $"http://127.0.0.1:{server.Port}/v1";
        provider.Protocol = protocol;
        provider.EnableWebSearch = true;
        provider.Model = "contract-model";
        using var client = new UniversalModelClient(provider, "model-key");
        var chunks = new List<string>();
        var result = await client.AnalyzeAsync(
            [new CapturedScreen(new Drawing.Rectangle(0, 0, 800, 600), 800, 600, Convert.ToBase64String("jpeg"u8.ToArray()))],
            "current question",
            [new ConversationTurn("prior question", "prior answer")],
            chunks.Add,
            CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(5));
        await server.Server.WaitAsync(TimeSpan.FromSeconds(5));
        return request with
        {
            Result = result,
            CallbacksValid = chunks.SequenceEqual(["hello ", "hello clicky"])
        };
    }
    finally
    {
        server.Listener.Stop();
    }
}

static async Task<(string Token, HttpProbe Request)> ProbeAssemblyTokenAsync()
{
    var server = StartHttpProbe("{\"token\":\"short lived/token+value\"}", "application/json");
    try
    {
        var worker = ClickyWorkerConfiguration.FromBaseUrl($"http://127.0.0.1:{server.Port}");
        var token = await AssemblyAiTranscriptionSession.FetchTokenAsync(worker, CancellationToken.None);
        return (token, await server.Request.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    finally
    {
        server.Listener.Stop();
    }
}

static async Task<string> ProbeUniversalModelErrorAsync()
{
    const string sse = "data: {\"type\":\"response.created\"}\n\ndata: {\"type\":\"error\",\"error\":{\"type\":\"insufficient_quota\",\"message\":\"quota exhausted\"}}\n\ndata: {\"type\":\"response.failed\",\"response\":{\"error\":{\"message\":\"quota exhausted\"}}}\n\n";
    var server = StartHttpProbe(sse, "text/event-stream");
    try
    {
        var provider = ProviderSettings.FromPreset("OpenAI");
        provider.BaseUrl = $"http://127.0.0.1:{server.Port}/v1";
        using var client = new UniversalModelClient(provider, "model-key");
        try
        {
            _ = await client.AnalyzeAsync([], "test", [], null, CancellationToken.None);
            return string.Empty;
        }
        catch (HttpRequestException exception)
        {
            return exception.Message;
        }
    }
    finally
    {
        server.Listener.Stop();
    }
}

static async Task<WorkerModelProbe> ProbeWorkerModelAsync()
{
    const string sse = "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"worker \"}}\n\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"reply\"}}\n\ndata: [DONE]\n\n";
    var server = StartHttpProbe(sse, "text/event-stream");
    try
    {
        using var client = new ClaudeWorkerClient(ClickyWorkerConfiguration.FromBaseUrl($"http://127.0.0.1:{server.Port}"));
        var result = await client.AnalyzeAsync(
            [new CapturedScreen(new Drawing.Rectangle(0, 0, 640, 480), 640, 480, Convert.ToBase64String("jpeg"u8.ToArray()))],
            "current question", "claude-contract", [new ConversationTurn("prior question", "prior answer")], null, CancellationToken.None);
        return new WorkerModelProbe(result, await server.Request.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    finally
    {
        server.Listener.Stop();
    }
}

static async Task<AudioProbe> ProbeDirectSpeechAsync()
{
    var server = StartHttpProbe("fake-mp3", "audio/mpeg");
    try
    {
        var audio = new AudioSettings { BaseUrl = $"http://127.0.0.1:{server.Port}/v1", SpeechModel = "tts-1-hd", Voice = "nova" };
        using var client = new OpenAiAudioClient(audio, ProviderSettings.FromPreset("OpenAI"), "speech-key");
        var bytes = await client.SynthesizeAsync("speak this", CancellationToken.None);
        return new AudioProbe(bytes, await server.Request.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    finally
    {
        server.Listener.Stop();
    }
}

static async Task<AudioProbe> ProbeWorkerSpeechAsync()
{
    var server = StartHttpProbe("worker-mp3", "audio/mpeg");
    try
    {
        using var client = new ElevenLabsTtsPlayer(ClickyWorkerConfiguration.FromBaseUrl($"http://127.0.0.1:{server.Port}"));
        var bytes = await client.SynthesizeAsync("speak this", CancellationToken.None);
        return new AudioProbe(bytes, await server.Request.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    finally
    {
        server.Listener.Stop();
    }
}

static HttpProbeServer StartHttpProbe(string responseBody, string contentType)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var requestSource = new TaskCompletionSource<HttpProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
    var server = Task.Run(async () =>
    {
        using var socket = await listener.AcceptTcpClientAsync();
        await using var stream = socket.GetStream();
        using var received = new MemoryStream();
        var buffer = new byte[8_192];
        var headerEnd = -1;
        var contentLength = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) break;
            received.Write(buffer, 0, count);
            var bytes = received.ToArray();
            if (headerEnd < 0)
            {
                headerEnd = FindSequence(bytes, "\r\n\r\n"u8.ToArray());
                if (headerEnd >= 0)
                {
                    var headers = Encoding.ASCII.GetString(bytes, 0, headerEnd);
                    var length = headers.Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    _ = int.TryParse(length?.Split(':', 2)[1].Trim(), out contentLength);
                }
            }
            if (headerEnd >= 0 && bytes.Length >= headerEnd + 4 + contentLength)
            {
                var headers = Encoding.ASCII.GetString(bytes, 0, headerEnd);
                var requestLine = headers.Split("\r\n")[0].Split(' ');
                var auth = headers.Split("\r\n").FirstOrDefault(line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim() ?? "";
                var body = Encoding.UTF8.GetString(bytes, headerEnd + 4, contentLength);
                requestSource.TrySetResult(new HttpProbe(requestLine[1], headers, auth, body, ""));
                break;
            }
        }
        var payload = Encoding.UTF8.GetBytes(responseBody);
        var responseHeaders = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHeaders);
        await stream.WriteAsync(payload);
    });
    return new HttpProbeServer(port, listener, requestSource.Task, server);
}

static int FindSequence(byte[] source, byte[] sequence)
{
    for (var index = 0; index <= source.Length - sequence.Length; index++)
    {
        var found = true;
        for (var offset = 0; offset < sequence.Length; offset++)
        {
            if (source[index + offset] != sequence[offset])
            {
                found = false;
                break;
            }
        }

        if (found)
        {
            return index;
        }
    }

    return -1;
}

static async Task<string> ProbeSmtpDeliveryAsync(AgentEmailProposal proposal)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var receivedMessage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var server = Task.Run(async () =>
    {
        using var socket = await listener.AcceptTcpClientAsync();
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true
        };
        await writer.WriteLineAsync("220 localhost Clicky SMTP probe");
        var data = new StringBuilder();
        var readingData = false;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (readingData)
            {
                if (line == ".")
                {
                    readingData = false;
                    receivedMessage.TrySetResult(data.ToString());
                    await writer.WriteLineAsync("250 accepted");
                }
                else
                {
                    data.AppendLine(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);
                }
                continue;
            }

            var command = line.Split(' ', 2)[0].ToUpperInvariant();
            if (command is "EHLO" or "HELO")
            {
                await writer.WriteLineAsync("250-localhost");
                await writer.WriteLineAsync("250 SIZE 1048576");
            }
            else if (command == "DATA")
            {
                readingData = true;
                await writer.WriteLineAsync("354 end with <CRLF>.<CRLF>");
            }
            else if (command == "QUIT")
            {
                await writer.WriteLineAsync("221 bye");
                break;
            }
            else
            {
                await writer.WriteLineAsync("250 ok");
            }
        }
    });

    try
    {
        var settings = new EmailSettings
        {
            Enabled = true,
            SmtpHost = "127.0.0.1",
            SmtpPort = port,
            Security = SmtpSecurityMode.None,
            FromAddress = "clicky@example.com",
            FromName = "Clicky"
        };
        await new SmtpEmailService().SendAsync(settings, null, proposal, CancellationToken.None);
        var message = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.WaitAsync(TimeSpan.FromSeconds(5));
        return message;
    }
    finally
    {
        listener.Stop();
    }
}

internal sealed record TranscriptionProbe(
    string Transcript,
    bool SawExpectedPath,
    bool SawAuthorization,
    bool SawWavePayload);

internal sealed record HttpProbe(string Path, string Headers, string Authorization, string Body, string Result)
{
    public bool CallbacksValid { get; init; }
}
internal sealed record HttpProbeServer(int Port, TcpListener Listener, Task<HttpProbe> Request, Task Server);
internal sealed record WorkerModelProbe(string Result, HttpProbe Request);
internal sealed record AudioProbe(byte[] Audio, HttpProbe Request);
