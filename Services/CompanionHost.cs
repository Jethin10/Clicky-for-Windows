using Clicky.Windows.Native;
using Clicky.Windows.Views;
using System.Windows;
using Drawing = System.Drawing;

namespace Clicky.Windows.Services;

/// <summary>
/// Owns the Windows equivalent of Clicky's public companion pipeline:
/// Ctrl+Alt hold -> microphone stream -> one-time screen capture -> Worker
/// chat -> TTS -> optional pointer flight.
/// </summary>
public sealed class CompanionHost : IDisposable
{
    private readonly ModifierPushToTalkMonitor _pushToTalk = new();
    private readonly MicrophoneCaptureService _microphone = new();
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly DocumentContextService _documentContextService = new();
    private readonly AgentWorkspaceService _agentWorkspaceService = new();
    private readonly SmtpEmailService _smtpEmailService = new();
    private readonly WindowsSpeechRecognitionService _localSpeechRecognition = new();
    private readonly WindowsSpeechSynthesisService _localSpeechSynthesis = new();
    private readonly OverlayHost _overlayHost = new();
    private readonly TrayService _trayService = new();
    private readonly CompanionPanelWindow _panel = new();
    private readonly ClickyWorkerConfiguration _worker = ClickyWorkerConfiguration.FromEnvironment();
    private readonly SettingsService _settingsService = new();
    private readonly SecureCredentialStore _credentialStore = new();
    private readonly List<ConversationTurn> _conversationHistory = [];
    private readonly object _sessionGate = new();
    private readonly List<PendingAudio> _bufferedAudio = [];
    private readonly MemoryStream _recordedPcm = new();
    private readonly SemaphoreSlim _audioSendGate = new(1, 1);
    private readonly SemaphoreSlim _agentQueueGate = new(1, 1);
    private readonly CancellationTokenSource _agentCancellation = new();
    private readonly ClaudeWorkerClient? _claude;
    private readonly ElevenLabsTtsPlayer? _tts;
    private ClickySettings _settings;
    private UniversalModelClient? _directModel;
    private OpenAiAudioClient? _directAudio;
    private DocumentContext? _attachedDocument;
    private AgentTaskResult? _latestAgentResult;
    private AgentResultWindow? _agentResultWindow;
    private OnboardingVideoWindow? _onboardingVideoWindow;
    private CancellationTokenSource? _onboardingCancellation;

    private AssemblyAiTranscriptionSession? _transcriptionSession;
    private Task? _transcriptionStartup;
    private CancellationTokenSource? _interactionCancellation;
    private bool _disposed;
    private int _queuedAgentTasks;

    public CompanionHost()
    {
        _settings = _settingsService.Load();
        if (_worker.IsConfigured)
        {
            _claude = new ClaudeWorkerClient(_worker);
            _tts = new ElevenLabsTtsPlayer(_worker);
        }

        ReloadDirectProvider();
    }

    public void Start()
    {
        _panel.StartRequested += StartOnboarding;
        _panel.OnboardingCompleted += PersistOnboardingCompleted;
        _panel.ReplayRequested += StartOnboarding;
        _panel.ScreenRecordingRequested += ValidateScreenCapture;
        _panel.SettingsRequested += OpenSettings;
        _panel.QuitRequested += Quit;
        _panel.ModelChanged += _ => { };
        _panel.PromptSubmitted += HandleTypedPrompt;
        _panel.AttachDocumentRequested += AttachDocument;
        _panel.RemoveDocumentRequested += RemoveDocument;
        _panel.ViewAgentResultRequested += ShowLatestAgentResult;
        _panel.SetWorkerConfigured(_worker.IsConfigured);
        _panel.SetOnboardingCompleted(_settings.OnboardingCompleted);
        RefreshProviderStatus();
        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_READY"), "1", StringComparison.Ordinal))
        {
            _panel.EnableVisualTestReadyState();
        }

        _trayService.OpenRequested += _panel.ShowPanel;
        _trayService.SettingsRequested += OpenSettings;
        _trayService.OverlayToggleRequested += ToggleOverlay;
        _trayService.QuitRequested += Quit;

        _microphone.AudioAvailable += QueueAudio;
        _microphone.CaptureFailed += OnMicrophoneFailure;

        _pushToTalk.Pressed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(StartListening);
        _pushToTalk.Released += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(SubmitInteraction);
        _pushToTalk.Start();

        _panel.ShowPanel();
        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_SETTINGS"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(OpenSettings);
        }

        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_AGENT_APPROVAL"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(ShowAgentApprovalVisualTest);
        }

        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_EMAIL_APPROVAL"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(ShowEmailApprovalVisualTest);
        }

        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_AGENT_RESULT"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(ShowAgentResultVisualTest);
        }

        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_OVERLAY"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(RunOverlayVisualTest);
        }

        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_PANEL_DISMISS"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(RunPanelDismissVisualTest);
        }
        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_PANEL_OWNED"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(RunOwnedWindowFocusVisualTest);
        }
    }

    private void PersistOnboardingCompleted()
    {
        if (_settings.OnboardingCompleted)
        {
            return;
        }

        _settings.OnboardingCompleted = true;
        _settingsService.Save(_settings);
    }

    private async void RunOverlayVisualTest()
    {
        _panel.Hide();
        _overlayHost.Show();
        _overlayHost.SetState(InteractionState.Idle);
        await Task.Delay(500);
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? new Drawing.Rectangle(0, 0, 1_280, 720);
        _overlayHost.PointAt(
            new Drawing.Point(screen.Left + 220, screen.Top + 220),
            "right here!");
    }

    private async void RunPanelDismissVisualTest()
    {
        await Task.Delay(500);
        var focusProbe = new Window
        {
            Width = 2,
            Height = 2,
            Left = -10_000,
            Top = -10_000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Opacity = 0.01,
            Topmost = true
        };
        focusProbe.Show();
        focusProbe.Activate();
        await Task.Delay(700);
        focusProbe.Close();
    }

    private async void RunOwnedWindowFocusVisualTest()
    {
        await Task.Delay(500);
        var ownedProbe = new Window
        {
            Owner = _panel,
            Width = 2,
            Height = 2,
            Left = -10_000,
            Top = -10_000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Opacity = 0.01,
            Topmost = true
        };
        ownedProbe.Show();
        ownedProbe.Activate();
        await Task.Delay(700);
        if (_panel.IsVisible)
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "clicky-panel-owned-preserved.txt"),
                "owned window preserved the panel");
        }
        ownedProbe.Close();
    }

    private void ValidateScreenCapture()
    {
        _ = _screenCapture.CaptureCursorScreen();
    }

    private void StartOnboarding()
    {
        StopOnboarding();
        CancelInteraction();
        _onboardingCancellation = new CancellationTokenSource();
        var cancellationToken = _onboardingCancellation.Token;
        _overlayHost.Show();
        _overlayHost.SetState(InteractionState.Idle);
        _panel.SetVoiceState(InteractionState.Idle);
        _panel.Hide();

        if (NativeMethods.GetCursorPos(out var cursor))
        {
            _overlayHost.PointAt(new Drawing.Point(cursor.X, cursor.Y), "hey! i'm clicky");
        }

        _onboardingVideoWindow = new OnboardingVideoWindow();
        _onboardingVideoWindow.PlaybackFinished += CompleteOnboardingVideo;
        _onboardingVideoWindow.StartFollowingCursor();
        _ = RunOnboardingDemoAsync(cancellationToken);
    }

    private async Task RunOnboardingDemoAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(40), cancellationToken);
            var captures = await Task.Run(_screenCapture.CaptureAllScreens, cancellationToken);
            if (captures.Count == 0)
            {
                return;
            }
            var response = await AnalyzeConfiguredProviderAsync(
                captures,
                "look at the current screen and choose one specific visible element near the center. make a playful observation in six words or fewer and point to it. [POINT:none] is allowed if nothing suitable is visible.",
                history: [],
                isolatedClient: true,
                cancellationToken);
            if (response is null)
            {
                return;
            }
            var pointing = PointerTagParser.Parse(response.Text);
            var target = PointerTagParser.MapToScreen(pointing, captures);
            if (target is { } point)
            {
                Dispatch(() =>
                {
                    _overlayHost.SetState(InteractionState.Idle);
                    _overlayHost.PointAt(point, string.IsNullOrWhiteSpace(pointing.SpokenText) ? "right here!" : pointing.SpokenText);
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Onboarding video and local fallback remain usable without a model.
        }
    }

    private void CompleteOnboardingVideo()
    {
        _onboardingVideoWindow = null;
        _onboardingCancellation?.Cancel();
        _onboardingCancellation?.Dispose();
        _onboardingCancellation = null;
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            _overlayHost.SetState(InteractionState.Idle);
            _overlayHost.PointAt(new Drawing.Point(cursor.X, cursor.Y), "hold Control+Alt and introduce yourself");
        }
    }

    private void StopOnboarding()
    {
        _onboardingCancellation?.Cancel();
        _onboardingCancellation?.Dispose();
        _onboardingCancellation = null;
        if (_onboardingVideoWindow is not null)
        {
            _onboardingVideoWindow.PlaybackFinished -= CompleteOnboardingVideo;
            _onboardingVideoWindow.Close();
            _onboardingVideoWindow = null;
        }
    }

    private void StartListening()
    {
        if (!_panel.IsOnboarded)
        {
            return;
        }

        StopOnboarding();
        CancelInteraction();
        _interactionCancellation = new CancellationTokenSource();
        var cancellationToken = _interactionCancellation.Token;
        lock (_sessionGate)
        {
            _recordedPcm.SetLength(0);
        }

        _overlayHost.Show();
        _overlayHost.SetAudioLevel(0);
        _overlayHost.SetState(InteractionState.Listening);
        _panel.SetVoiceState(InteractionState.Listening);
        _panel.Hide();

        // The microphone starts immediately. Audio is kept briefly in memory
        // while the transcription WebSocket obtains its short-lived token.
        _microphone.Start();
        if (_directAudio is null && _worker.IsConfigured)
        {
            _transcriptionStartup = StartTranscriptionAsync(cancellationToken);
        }
    }

    private async Task StartTranscriptionAsync(CancellationToken cancellationToken)
    {
        AssemblyAiTranscriptionSession? session = null;
        try
        {
            session = await AssemblyAiTranscriptionSession.ConnectAsync(_worker, cancellationToken);
            session.AudioLevelChanged += level => Dispatch(() => _overlayHost.SetAudioLevel(level));

            List<PendingAudio> pending;
            lock (_sessionGate)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    pending = [];
                }
                else
                {
                    _transcriptionSession = session;
                    pending = [.. _bufferedAudio];
                    _bufferedAudio.Clear();
                    session = null;
                }
            }

            if (session is not null)
            {
                await session.DisposeAsync();
                return;
            }

            foreach (var audio in pending)
            {
                await SendAudioAsync(audio.Bytes, audio.Level, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }
        catch (Exception)
        {
            if (!CanUseLocalSpeechRecognition())
            {
                Dispatch(() => ShowRecoverableMessage("voice connection didn't start"));
            }
        }
    }

    private void QueueAudio(ReadOnlyMemory<byte> pcm16, float level)
    {
        Dispatch(() => _overlayHost.SetAudioLevel(level));
        if (pcm16.IsEmpty)
        {
            return;
        }

        var copy = pcm16.ToArray();
        lock (_sessionGate)
        {
            // Five minutes of 16 kHz, mono, 16-bit PCM is about 9.6 MB.
            if (_recordedPcm.Length + copy.Length <= 10_000_000)
            {
                _recordedPcm.Write(copy, 0, copy.Length);
            }
        }

        if (_directAudio is not null || !_worker.IsConfigured)
        {
            return;
        }

        var shouldBuffer = false;
        lock (_sessionGate)
        {
            if (_transcriptionSession is null)
            {
                // 50 ms blocks, bounded to 15 seconds while the connection warms.
                if (_bufferedAudio.Count >= 300)
                {
                    _bufferedAudio.RemoveAt(0);
                }

                _bufferedAudio.Add(new PendingAudio(copy, level));
                shouldBuffer = true;
            }
        }

        if (!shouldBuffer && _interactionCancellation is { IsCancellationRequested: false } cancellation)
        {
            _ = SendAudioAsync(copy, level, cancellation.Token);
        }
    }

    private async Task SendAudioAsync(byte[] pcm16, float level, CancellationToken cancellationToken)
    {
        await _audioSendGate.WaitAsync(cancellationToken);
        try
        {
            AssemblyAiTranscriptionSession? session;
            lock (_sessionGate)
            {
                session = _transcriptionSession;
            }

            if (session is not null)
            {
                await session.SendAudioAsync(pcm16, level, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A later submit reports a concise user-facing recovery message.
        }
        finally
        {
            _audioSendGate.Release();
        }
    }

    private void SubmitInteraction()
    {
        if (_interactionCancellation is null || _interactionCancellation.IsCancellationRequested)
        {
            return;
        }

        _microphone.Stop();
        _ = CompleteInteractionAsync(_interactionCancellation.Token);
    }

    private async Task CompleteInteractionAsync(CancellationToken cancellationToken)
    {
        try
        {
            _overlayHost.SetState(InteractionState.Processing);
            _panel.SetVoiceState(InteractionState.Processing);

            if (_directAudio is null && !_worker.IsConfigured && !CanUseLocalSpeechRecognition())
            {
                ShowRecoverableMessage("enable direct voice, configure CLICKY_WORKER_URL, or install Windows speech recognition");
                return;
            }

            if (_directModel is null && _claude is null)
            {
                ShowRecoverableMessage("configure an AI provider in Clicky settings");
                return;
            }

            // WaveInEvent can deliver its final buffer immediately after
            // StopRecording; give that callback one dispatcher turn.
            await Task.Delay(80, cancellationToken);
            var recordedPcm = TakeRecordedPcm();
            if (recordedPcm.Length < 1_600)
            {
                ShowRecoverableMessage("i didn't catch that");
                return;
            }

            var transcript = await TranscribeWithFallbackAsync(recordedPcm, cancellationToken);

            if (string.IsNullOrWhiteSpace(transcript))
            {
                ShowRecoverableMessage("i didn't catch that");
                return;
            }

            if (VoiceCommandRouter.TryExtractAgentCommand(transcript, out var agentCommand))
            {
                if (string.IsNullOrWhiteSpace(agentCommand))
                {
                    ShowRecoverableMessage("tell me what the agent should do");
                    return;
                }

                _overlayHost.SetState(InteractionState.Idle);
                _panel.SetVoiceState(InteractionState.Idle);
                _panel.SetAgentStatus("Voice agent queued", active: true);
                _ = RunBackgroundAgentAsync(
                    DocumentContextService.AddToPrompt(agentCommand, _attachedDocument),
                    _agentCancellation.Token);
                return;
            }

            // Capture only after the final transcript, never continuously. Hide
            // Clicky's own transparent windows for a frame so they cannot appear
            // in a capture on Windows editions that ignore display affinity.
            _overlayHost.Hide();
            var captures = await Task.Run(_screenCapture.CaptureAllScreens, cancellationToken);
            _overlayHost.Show();
            _overlayHost.SetState(InteractionState.Processing);
            if (captures.Count == 0)
            {
                ShowRecoverableMessage("i couldn't read that screen");
                return;
            }

            var history = _conversationHistory.TakeLast(10).ToList();
            var requestTranscript = DocumentContextService.AddToPrompt(transcript, _attachedDocument);
            var response = _directModel is not null
                ? await _directModel.AnalyzeAsync(
                    captures,
                    requestTranscript,
                    history,
                    onTextChunk: null,
                    cancellationToken)
                : await _claude!.AnalyzeAsync(
                    captures,
                    requestTranscript,
                    _panel.SelectedModel,
                    history,
                    onTextChunk: null,
                    cancellationToken);

            var pointing = PointerTagParser.Parse(response);
            var spokenText = pointing.SpokenText;
            if (string.IsNullOrWhiteSpace(spokenText))
            {
                ShowRecoverableMessage("i lost my words there");
                return;
            }

            _conversationHistory.Add(new ConversationTurn(transcript, spokenText));
            if (_conversationHistory.Count > 10)
            {
                _conversationHistory.RemoveRange(0, _conversationHistory.Count - 10);
            }

            _overlayHost.SetState(InteractionState.Responding);
            _panel.SetVoiceState(InteractionState.Responding);
            var target = PointerTagParser.MapToScreen(pointing, captures);
            if (target is { } point)
            {
                _overlayHost.PointAt(point, spokenText.Length > 42 ? "right here!" : spokenText);
            }

            await SpeakConfiguredAsync(spokenText, cancellationToken);

            _overlayHost.SetState(InteractionState.Idle);
            _panel.SetVoiceState(InteractionState.Idle);
        }
        catch (OperationCanceledException)
        {
            // A new Ctrl+Alt hold intentionally interrupts recording, speech and pointing.
        }
        catch (Exception exception)
        {
            ShowRecoverableMessage($"voice error: {ShortMessage(exception.Message)}");
        }
        finally
        {
            _microphone.Stop();
            lock (_sessionGate)
            {
                _recordedPcm.SetLength(0);
            }
        }
    }

    private async Task<string> TranscribeWithFallbackAsync(byte[] recordedPcm, CancellationToken cancellationToken)
    {
        return await TranscriptionFallbackPolicy.ExecuteAsync(
            TranscribePrimaryAsync,
            token => _localSpeechRecognition.RecognizePcm16Async(
                recordedPcm,
                _settings.Audio.WindowsSpeechCulture,
                token),
            CanUseLocalSpeechRecognition(),
            cancellationToken);

        async Task<string> TranscribePrimaryAsync(CancellationToken token)
        {
            if (_directAudio is not null)
            {
                return await _directAudio.TranscribeAsync(recordedPcm, token);
            }
            if (_worker.IsConfigured)
            {
                if (_transcriptionStartup is not null)
                {
                    await _transcriptionStartup.WaitAsync(TimeSpan.FromSeconds(14), token);
                }

                var session = TakeTranscriptionSession()
                    ?? throw new InvalidOperationException("Voice connection wasn't ready.");
                try
                {
                    return await session.FinalizeAsync(token);
                }
                finally
                {
                    await session.DisposeAsync();
                }
            }
            return string.Empty;
        }
    }

    private bool CanUseLocalSpeechRecognition() =>
        _settings.Audio.EnableWindowsSpeechFallback && WindowsSpeechRecognitionService.IsAvailable;

    private AssemblyAiTranscriptionSession? TakeTranscriptionSession()
    {
        lock (_sessionGate)
        {
            var session = _transcriptionSession;
            _transcriptionSession = null;
            _bufferedAudio.Clear();
            return session;
        }
    }

    private byte[] TakeRecordedPcm()
    {
        lock (_sessionGate)
        {
            var bytes = _recordedPcm.ToArray();
            _recordedPcm.SetLength(0);
            return bytes;
        }
    }

    private void ShowRecoverableMessage(string message)
    {
        _overlayHost.SetState(InteractionState.Idle);
        _panel.SetVoiceState(InteractionState.Idle);
        _overlayHost.Show();
        _overlayHost.PointAt(GetCursorPoint(), message);
    }

    private static Drawing.Point GetCursorPoint()
    {
        return NativeMethods.GetCursorPos(out var cursor)
            ? new Drawing.Point(cursor.X, cursor.Y)
            : Drawing.Point.Empty;
    }

    private void ToggleOverlay()
    {
        if (_overlayHost.IsVisible)
        {
            _overlayHost.Hide();
        }
        else
        {
            _overlayHost.Show();
        }
    }

    private void HandleTypedPrompt(string prompt, bool agentMode)
    {
        var requestPrompt = DocumentContextService.AddToPrompt(prompt, _attachedDocument);
        if (agentMode)
        {
            _ = RunBackgroundAgentAsync(requestPrompt, _agentCancellation.Token);
        }
        else
        {
            _ = CompleteTypedPromptAsync(requestPrompt, prompt, _agentCancellation.Token);
        }
    }

    private async void AttachDocument()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Attach a document to Clicky",
            Filter = "Documents and images (*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.txt;*.md;*.json;*.csv;*.log;*.cs;*.xaml;*.xml;*.html;*.css;*.js;*.ts;*.py)|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.txt;*.md;*.json;*.csv;*.log;*.cs;*.xaml;*.xml;*.html;*.css;*.js;*.ts;*.py|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(_panel) != true)
        {
            return;
        }

        var fileName = Path.GetFileName(dialog.FileName);
        _panel.SetAttachmentStatus(fileName, "Extracting locally...", visible: true);
        try
        {
            var context = await _documentContextService.ExtractAsync(dialog.FileName, _agentCancellation.Token);
            _attachedDocument = context;
            var detail = context.PageCount is { } pages
                ? $"{pages} page{(pages == 1 ? string.Empty : "s")}, {context.Text.Length:N0} characters"
                : $"{context.Text.Length:N0} characters";
            if (context.WasTruncated)
            {
                detail += ", safely truncated";
            }
            if (context.UsedLocalOcr)
            {
                detail += ", local OCR";
            }
            _panel.SetAttachmentStatus(context.FileName, detail, visible: true);
        }
        catch (Exception exception)
        {
            _attachedDocument = null;
            _panel.SetAttachmentStatus(fileName, ShortMessage(exception.Message), visible: true);
        }
    }

    private void RemoveDocument()
    {
        _attachedDocument = null;
        _panel.SetAttachmentStatus(string.Empty, string.Empty, visible: false);
    }

    private async Task<ProviderResponse?> AnalyzeConfiguredProviderAsync(
        IReadOnlyList<CapturedScreen> captures,
        string prompt,
        IReadOnlyList<ConversationTurn> history,
        bool isolatedClient,
        CancellationToken cancellationToken)
    {
        if (_directModel is not null)
        {
            if (!isolatedClient)
            {
                return new ProviderResponse(
                    await _directModel.AnalyzeAsync(captures, prompt, history, null, cancellationToken),
                    _settings.Provider.DisplayName);
            }

            var settingsSnapshot = _settingsService.Load();
            using var client = new UniversalModelClient(settingsSnapshot.Provider, _credentialStore.ReadApiKey());
            return new ProviderResponse(
                await client.AnalyzeAsync(captures, prompt, history, null, cancellationToken),
                settingsSnapshot.Provider.DisplayName);
        }

        if (_claude is not null)
        {
            return new ProviderResponse(
                await _claude.AnalyzeAsync(captures, prompt, _panel.SelectedModel, history, null, cancellationToken),
                "Private Worker");
        }

        return null;
    }

    private async Task<IReadOnlyList<CapturedScreen>> CaptureCurrentScreensAsync(CancellationToken cancellationToken)
    {
        _overlayHost.Hide();
        try
        {
            return await Task.Run(_screenCapture.CaptureAllScreens, cancellationToken);
        }
        finally
        {
            _overlayHost.Show();
        }
    }

    private async Task CompleteTypedPromptAsync(
        string requestPrompt,
        string historyPrompt,
        CancellationToken cancellationToken)
    {
        try
        {
            _panel.SetVoiceState(InteractionState.Processing);
            _panel.Hide();
            _overlayHost.Show();
            _overlayHost.SetState(InteractionState.Processing);
            var captures = await CaptureCurrentScreensAsync(cancellationToken);
            if (captures.Count == 0)
            {
                ShowRecoverableMessage("i couldn't read that screen");
                return;
            }

            var response = await AnalyzeConfiguredProviderAsync(
                captures,
                requestPrompt,
                _conversationHistory.TakeLast(10).ToList(),
                isolatedClient: false,
                cancellationToken);
            if (response is null)
            {
                ShowRecoverableMessage("configure an AI provider in Clicky settings");
                return;
            }

            await PresentResponseAsync(historyPrompt, response.Text, captures, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowRecoverableMessage($"provider error: {ShortMessage(exception.Message)}");
        }
    }

    private async Task RunBackgroundAgentAsync(string prompt, CancellationToken cancellationToken)
    {
        var queueEntered = false;
        var queueCountCleared = false;
        var queuePosition = Interlocked.Increment(ref _queuedAgentTasks);
        Dispatch(() => _panel.SetAgentStatus(
            queuePosition == 1 ? "Agent queued" : $"{queuePosition} agent tasks queued",
            active: true));

        try
        {
            var captures = await CaptureCurrentScreensAsync(cancellationToken);
            await _agentQueueGate.WaitAsync(cancellationToken);
            queueEntered = true;
            Interlocked.Decrement(ref _queuedAgentTasks);
            queueCountCleared = true;
            Dispatch(() => _panel.SetAgentStatus("Agent working in the background...", active: true));

            var agentPrompt = $$"""
                background agent request: {{prompt}}

                complete the research, analysis, or artifact drafting that is possible with your available provider tools. do not claim to have edited files, sent messages, clicked controls, or completed external actions unless an actual tool result proves it. return the useful finished result, not a plan.

                when the user asks you to build or create files, include a machine-readable package using exactly this shape, with valid JSON and relative paths only:
                [CLICKY_FILES]
                {"summary":"what the files implement","files":[{"path":"index.html","content":"complete file contents"}]}
                [/CLICKY_FILES]
                include at most twenty files. never include absolute paths, parent-directory traversal, binaries, commands, or claims that the files were already written. clicky will show the user every proposed file and require approval before writing. [POINT:none]

                when the user explicitly asks to draft or send an email and supplies the recipients, include exactly one machine-readable proposal using this shape with valid JSON:
                [CLICKY_EMAIL]
                {"to":["person@example.com"],"cc":[],"subject":"Subject","body":"Complete plain-text message"}
                [/CLICKY_EMAIL]
                never invent recipients or claim the email was sent. clicky will show the complete proposal and require explicit approval before SMTP delivery.
                """;
            var response = await AnalyzeConfiguredProviderAsync(
                captures,
                agentPrompt,
                history: [],
                isolatedClient: true,
                cancellationToken);
            if (response is null)
            {
                Dispatch(() => _panel.SetAgentStatus("Agent needs an AI provider configuration", active: false));
                return;
            }

            var artifactResult = AgentArtifactParser.Parse(response.Text);
            var emailResult = AgentEmailParser.Parse(artifactResult.VisibleText);
            var result = PointerTagParser.Parse(emailResult.VisibleText).SpokenText;
            if (artifactResult.Package is { } package)
            {
                var writeResult = await ReviewAndWriteAgentFilesAsync(package, cancellationToken);
                if (writeResult is not null)
                {
                    result = $"{package.Summary}. Wrote {writeResult.FilesWritten} file{(writeResult.FilesWritten == 1 ? string.Empty : "s")} to {writeResult.WorkspacePath}.";
                }
                else
                {
                    result = "The file proposal was not written.";
                }
            }
            if (emailResult.Proposal is { } email)
            {
                var sent = await ReviewAndSendEmailAsync(email, cancellationToken);
                result = sent
                    ? $"Email sent to {string.Join(", ", email.To)}."
                    : "The email proposal was not sent.";
            }
            if (string.IsNullOrWhiteSpace(result))
            {
                result = artifactResult.Package?.Summary ?? "Agent finished.";
            }
            var completedResult = AgentResultService.Create(
                prompt,
                response.ProviderName,
                result,
                DateTimeOffset.Now);
            Dispatch(() =>
            {
                _agentResultWindow?.Close();
                _agentResultWindow = null;
                _latestAgentResult = completedResult;
                _panel.SetAgentStatus($"Agent finished via {response.ProviderName}", active: false);
                _panel.SetAgentResult(
                    response.ProviderName,
                    AgentResultService.Preview(result));
                _overlayHost.Show();
                _overlayHost.PointAt(GetCursorPoint(), "agent finished — open the full result in Clicky");
            });
        }
        catch (OperationCanceledException)
        {
            Dispatch(() => _panel.SetAgentStatus("Agent stopped", active: false));
        }
        catch (Exception exception)
        {
            Dispatch(() => _panel.SetAgentStatus($"Agent failed: {ShortMessage(exception.Message)}", active: false));
        }
        finally
        {
            if (!queueCountCleared)
            {
                Interlocked.Decrement(ref _queuedAgentTasks);
            }

            if (queueEntered)
            {
                _agentQueueGate.Release();
            }
        }
    }

    private void ShowLatestAgentResult()
    {
        if (_latestAgentResult is null)
        {
            return;
        }
        if (_agentResultWindow is { IsLoaded: true })
        {
            _agentResultWindow.Activate();
            return;
        }

        _agentResultWindow = new AgentResultWindow(_latestAgentResult) { Owner = _panel };
        _agentResultWindow.Closed += (_, _) => _agentResultWindow = null;
        _agentResultWindow.Show();
    }

    private void ShowAgentResultVisualTest()
    {
        _latestAgentResult = new AgentTaskResult(
            "Find cameras like the one on my screen under $1,000 and compare the strongest options.",
            "OpenRouter · web search",
            "Research complete\n\n1. Sony ZV-E10 II — strong autofocus, interchangeable lenses, and excellent creator-focused video tools. Typical body pricing leaves room for a starter lens.\n\n2. Canon EOS R50 — compact, approachable controls, reliable subject detection, and good 4K output.\n\n3. Fujifilm X-S20 — the most capable hybrid option when discounted, with stabilization and strong battery life.\n\nRecommendation: choose the R50 for the simplest setup, or the ZV-E10 II when lens flexibility and video autofocus matter most. Verify current retailer pricing and included lens bundles before purchasing.",
            DateTimeOffset.Now);
        _panel.SetAgentResult("OpenRouter · web search", "Research complete — compared Sony, Canon, and Fujifilm options.");
        ShowLatestAgentResult();
    }

    private async Task<bool> ReviewAndSendEmailAsync(
        AgentEmailProposal proposal,
        CancellationToken cancellationToken)
    {
        if (!_settings.Email.Enabled || !_settings.Email.IsConfigured)
        {
            _panel.SetAgentStatus("Configure and enable SMTP email delivery in Settings", active: false);
            return false;
        }

        var approval = new EmailApprovalWindow(_settings.Email, proposal) { Owner = _panel };
        if (approval.ShowDialog() != true || !approval.Approved)
        {
            return false;
        }

        _panel.SetAgentStatus("Sending approved email...", active: true);
        await _smtpEmailService.SendAsync(
            _settings.Email,
            _credentialStore.ReadEmailPassword(),
            proposal,
            cancellationToken);
        return true;
    }

    private async Task<AgentWriteResult?> ReviewAndWriteAgentFilesAsync(
        AgentArtifactPackage package,
        CancellationToken cancellationToken)
    {
        var workspace = _settings.Agent.WorkspacePath;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
        {
            using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose where Clicky may write these approved agent files",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return null;
            }

            workspace = folderDialog.SelectedPath;
            _settings.Agent.WorkspacePath = workspace;
            _settingsService.Save(_settings);
        }

        var plans = _agentWorkspaceService.Plan(workspace, package);
        var approval = new AgentFileApprovalWindow(workspace, package, plans)
        {
            Owner = _panel
        };
        if (approval.ShowDialog() != true || !approval.Approved)
        {
            return null;
        }

        _panel.SetAgentStatus("Writing approved files...", active: true);
        return await _agentWorkspaceService.ApplyAsync(workspace, plans, cancellationToken);
    }

    private void ShowAgentApprovalVisualTest()
    {
        var workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Clicky Workspace");
        var package = new AgentArtifactPackage(
            "Responsive product page generated from the current screen",
            [
                new AgentFileArtifact("index.html", "<!doctype html>\n<html>\n  <body>\n    <main>Clicky preview</main>\n  </body>\n</html>"),
                new AgentFileArtifact("styles/site.css", "body { margin: 0; font-family: system-ui; }")
            ]);
        var plans = new[]
        {
            new AgentFilePlan("index.html", Path.Combine(workspace, "index.html"), package.Files[0].Content, false),
            new AgentFilePlan("styles\\site.css", Path.Combine(workspace, "styles", "site.css"), package.Files[1].Content, true)
        };
        var window = new AgentFileApprovalWindow(workspace, package, plans) { Owner = _panel };
        _ = window.ShowDialog();
    }

    private void ShowEmailApprovalVisualTest()
    {
        var settings = new EmailSettings
        {
            Enabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            FromAddress = "clicky@example.com",
            FromName = "Clicky"
        };
        var proposal = new AgentEmailProposal(
            ["alex@example.com"],
            ["team@example.com"],
            "Project update and next steps",
            "Hi Alex,\n\nThe Windows build is ready for review. The key workflows now pass their local verification checks.\n\nBest,\nClicky");
        var window = new EmailApprovalWindow(settings, proposal) { Owner = _panel };
        _ = window.ShowDialog();
    }

    private async Task PresentResponseAsync(
        string prompt,
        string rawResponse,
        IReadOnlyList<CapturedScreen> captures,
        CancellationToken cancellationToken)
    {
        var pointing = PointerTagParser.Parse(rawResponse);
        var text = pointing.SpokenText;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowRecoverableMessage("i lost my words there");
            return;
        }

        _conversationHistory.Add(new ConversationTurn(prompt, text));
        if (_conversationHistory.Count > 10)
        {
            _conversationHistory.RemoveRange(0, _conversationHistory.Count - 10);
        }

        _overlayHost.Show();
        _overlayHost.SetState(InteractionState.Responding);
        _panel.SetVoiceState(InteractionState.Responding);
        var target = PointerTagParser.MapToScreen(pointing, captures) ?? GetCursorPoint();
        var pointerPhrase = pointing.Pixel is null ? text : "right here!";
        _overlayHost.PointAt(target, pointerPhrase);

        await SpeakConfiguredAsync(text, cancellationToken);

        _overlayHost.SetState(InteractionState.Idle);
        _panel.SetVoiceState(InteractionState.Idle);
    }

    private static string ShortMessage(string message) => message.Length <= 120 ? message : message[..117] + "...";

    private async Task SpeakConfiguredAsync(string text, CancellationToken cancellationToken)
    {
        Exception? cloudFailure = null;
        if (_directAudio is not null)
        {
            try
            {
                await _directAudio.SpeakAsync(text, cancellationToken);
                while (_directAudio.IsPlaying)
                {
                    await Task.Delay(100, cancellationToken);
                }
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                cloudFailure = exception;
            }
        }

        if (_tts is not null)
        {
            try
            {
                await _tts.SpeakAsync(text, cancellationToken);
                while (_tts.IsPlaying)
                {
                    await Task.Delay(100, cancellationToken);
                }
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                cloudFailure = exception;
            }
        }

        if (_settings.Audio.EnableWindowsSpeechFallback && WindowsSpeechSynthesisService.IsAvailable)
        {
            await _localSpeechSynthesis.SpeakAsync(text, _settings.Audio.WindowsSpeechCulture, cancellationToken);
            return;
        }

        if (cloudFailure is not null)
        {
            throw new InvalidOperationException($"Cloud and Windows voice output failed: {cloudFailure.Message}", cloudFailure);
        }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settingsService, _credentialStore)
        {
            Owner = _panel
        };
        window.SettingsSaved += settings =>
        {
            _settings = settings;
            ReloadDirectProvider();
            RefreshProviderStatus();
        };
        _ = window.ShowDialog();
    }

    private void ReloadDirectProvider()
    {
        _directModel?.Dispose();
        _directModel = null;
        _directAudio?.Dispose();
        _directAudio = null;

        var apiKey = _credentialStore.ReadApiKey();
        var audioApiKey = _credentialStore.ReadAudioApiKey() ?? apiKey;
        var presetAllowsNoKey = _settings.Provider.Preset is "Local" or "Custom";
        var audioAllowsNoKey = presetAllowsNoKey
            || (Uri.TryCreate(_settings.Audio.EffectiveBaseUrl(_settings.Provider), UriKind.Absolute, out var audioUri)
                && audioUri.IsLoopback);
        if (_settings.Provider.IsConfigured && (presetAllowsNoKey || !string.IsNullOrWhiteSpace(apiKey)))
        {
            _directModel = new UniversalModelClient(_settings.Provider, apiKey);
        }

        if (_settings.Audio.IsConfigured(_settings.Provider)
            && (audioAllowsNoKey || !string.IsNullOrWhiteSpace(audioApiKey)))
        {
            _directAudio = new OpenAiAudioClient(_settings.Audio, _settings.Provider, audioApiKey);
        }
    }

    private void RefreshProviderStatus()
    {
        _panel.SetProviderConfiguration(
            _settings.Provider,
            _directModel is not null,
            _worker.IsConfigured,
            _directAudio is not null,
            CanUseLocalSpeechRecognition() && _settings.Audio.EnableWindowsSpeechFallback && WindowsSpeechSynthesisService.IsAvailable);
    }

    private void OnMicrophoneFailure(Exception _)
    {
        Dispatch(() => ShowRecoverableMessage("microphone isn't available"));
    }

    private void CancelInteraction()
    {
        _interactionCancellation?.Cancel();
        _interactionCancellation?.Dispose();
        _interactionCancellation = null;
        _microphone.Stop();
        _tts?.Stop();
        _directAudio?.Stop();
        lock (_sessionGate)
        {
            _recordedPcm.SetLength(0);
        }

        var session = TakeTranscriptionSession();
        if (session is not null)
        {
            _ = session.DisposeAsync();
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.BeginInvoke(action);
        }
    }

    private static void Quit() => System.Windows.Application.Current.Shutdown();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _agentCancellation.Cancel();
        StopOnboarding();
        CancelInteraction();
        _pushToTalk.Dispose();
        _microphone.Dispose();
        _agentResultWindow?.Close();
        _overlayHost.Dispose();
        _trayService.Dispose();
        _claude?.Dispose();
        _directModel?.Dispose();
        _directAudio?.Dispose();
        _tts?.Dispose();
        _recordedPcm.Dispose();
        _audioSendGate.Dispose();
        _agentQueueGate.Dispose();
        _agentCancellation.Dispose();
    }

    private sealed record PendingAudio(byte[] Bytes, float Level);
    private sealed record ProviderResponse(string Text, string ProviderName);
}
