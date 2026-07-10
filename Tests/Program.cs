using Clicky.Windows.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Drawing = System.Drawing;

var failures = new List<string>();

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
Check(audio.IsConfigured(provider), "default direct-audio settings");
Check(audio.EffectiveBaseUrl(provider) == provider.BaseUrl, "audio inherits provider base URL");

var transcriptionProbe = await ProbeTranscriptionEndpointAsync();
Check(transcriptionProbe.Transcript == "voice agent test", "direct transcription response parsing");
Check(transcriptionProbe.SawExpectedPath, "direct transcription endpoint path");
Check(transcriptionProbe.SawAuthorization, "direct transcription bearer authentication");
Check(transcriptionProbe.SawWavePayload, "direct transcription WAV upload");

var point = PointerTagParser.Parse("right here [POINT:320,240:button:screen2]");
Check(point.SpokenText == "right here", "pointer spoken text");
Check(point.Pixel == new Drawing.Point(320, 240), "pointer coordinates");
Check(point.ScreenNumber == 2, "pointer screen number");

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

var pdfFixture = Environment.GetEnvironmentVariable("CLICKY_TEST_PDF");
if (!string.IsNullOrWhiteSpace(pdfFixture) && File.Exists(pdfFixture))
{
    var pdfContext = await documentService.ExtractAsync(pdfFixture, CancellationToken.None);
    Check(pdfContext.PageCount == 2, "PDF page count");
    Check(pdfContext.Text.Contains("Clicky PDF verification", StringComparison.Ordinal), "PDF text extraction");
    Check(pdfContext.Text.Contains("second page confirms extraction order", StringComparison.OrdinalIgnoreCase), "PDF multi-page extraction");
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

internal sealed record TranscriptionProbe(
    string Transcript,
    bool SawExpectedPath,
    bool SawAuthorization,
    bool SawWavePayload);
