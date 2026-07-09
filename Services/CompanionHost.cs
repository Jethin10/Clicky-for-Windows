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
    private readonly OverlayHost _overlayHost = new();
    private readonly TrayService _trayService = new();
    private readonly CompanionPanelWindow _panel = new();
    private readonly ClickyWorkerConfiguration _worker = ClickyWorkerConfiguration.FromEnvironment();
    private readonly List<ConversationTurn> _conversationHistory = [];
    private readonly object _sessionGate = new();
    private readonly List<PendingAudio> _bufferedAudio = [];
    private readonly SemaphoreSlim _audioSendGate = new(1, 1);
    private readonly ClaudeWorkerClient? _claude;
    private readonly ElevenLabsTtsPlayer? _tts;

    private AssemblyAiTranscriptionSession? _transcriptionSession;
    private Task? _transcriptionStartup;
    private CancellationTokenSource? _interactionCancellation;
    private bool _disposed;

    public CompanionHost()
    {
        if (_worker.IsConfigured)
        {
            _claude = new ClaudeWorkerClient(_worker);
            _tts = new ElevenLabsTtsPlayer(_worker);
        }
    }

    public void Start()
    {
        _panel.StartRequested += StartOnboarding;
        _panel.ReplayRequested += StartOnboarding;
        _panel.ScreenRecordingRequested += ValidateScreenCapture;
        _panel.QuitRequested += Quit;
        _panel.ModelChanged += _ => { };
        _panel.SetWorkerConfigured(_worker.IsConfigured);
        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_READY"), "1", StringComparison.Ordinal))
        {
            _panel.EnableVisualTestReadyState();
        }

        _trayService.OpenRequested += _panel.ShowPanel;
        _trayService.OverlayToggleRequested += ToggleOverlay;
        _trayService.QuitRequested += Quit;

        _microphone.AudioAvailable += QueueAudio;
        _microphone.CaptureFailed += OnMicrophoneFailure;

        _pushToTalk.Pressed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(StartListening);
        _pushToTalk.Released += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(SubmitInteraction);
        _pushToTalk.Start();

        _panel.ShowPanel();
        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_OVERLAY"), "1", StringComparison.Ordinal))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(StartOnboarding);
        }
    }

    private void ValidateScreenCapture()
    {
        _ = _screenCapture.CaptureCursorScreen();
    }

    private void StartOnboarding()
    {
        CancelInteraction();
        _overlayHost.Show();
        _overlayHost.SetState(InteractionState.Idle);
        _panel.SetVoiceState(InteractionState.Idle);
        _panel.Hide();

        if (NativeMethods.GetCursorPos(out var cursor))
        {
            _overlayHost.PointAt(new Drawing.Point(cursor.X, cursor.Y), "hey! i'm clicky");
        }
    }

    private void StartListening()
    {
        if (!_panel.IsOnboarded)
        {
            return;
        }

        CancelInteraction();
        _interactionCancellation = new CancellationTokenSource();
        var cancellationToken = _interactionCancellation.Token;

        _overlayHost.Show();
        _overlayHost.SetAudioLevel(0);
        _overlayHost.SetState(InteractionState.Listening);
        _panel.SetVoiceState(InteractionState.Listening);
        _panel.Hide();

        // The microphone starts immediately. Audio is kept briefly in memory
        // while the transcription WebSocket obtains its short-lived token.
        _microphone.Start();
        if (_worker.IsConfigured)
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
            Dispatch(() => ShowRecoverableMessage("voice connection didn't start"));
        }
    }

    private void QueueAudio(ReadOnlyMemory<byte> pcm16, float level)
    {
        Dispatch(() => _overlayHost.SetAudioLevel(level));
        if (!_worker.IsConfigured || pcm16.IsEmpty)
        {
            return;
        }

        var copy = pcm16.ToArray();
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

            if (!_worker.IsConfigured || _claude is null || _tts is null)
            {
                ShowRecoverableMessage("set CLICKY_WORKER_URL to enable voice replies");
                return;
            }

            if (_transcriptionStartup is not null)
            {
                await _transcriptionStartup.WaitAsync(TimeSpan.FromSeconds(14), cancellationToken);
            }

            var session = TakeTranscriptionSession();
            if (session is null)
            {
                ShowRecoverableMessage("voice connection wasn't ready");
                return;
            }

            string transcript;
            try
            {
                transcript = await session.FinalizeAsync(cancellationToken);
            }
            finally
            {
                await session.DisposeAsync();
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                ShowRecoverableMessage("i didn't catch that");
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

            var response = await _claude.AnalyzeAsync(
                captures,
                transcript,
                _panel.SelectedModel,
                _conversationHistory.TakeLast(10).ToList(),
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

            await _tts.SpeakAsync(spokenText, cancellationToken);
            while (_tts.IsPlaying)
            {
                await Task.Delay(100, cancellationToken);
            }

            _overlayHost.SetState(InteractionState.Idle);
            _panel.SetVoiceState(InteractionState.Idle);
        }
        catch (OperationCanceledException)
        {
            // A new Ctrl+Alt hold intentionally interrupts recording, speech and pointing.
        }
        catch (Exception)
        {
            ShowRecoverableMessage("i hit a snag — try that again");
        }
        finally
        {
            _microphone.Stop();
        }
    }

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
        CancelInteraction();
        _pushToTalk.Dispose();
        _microphone.Dispose();
        _overlayHost.Dispose();
        _trayService.Dispose();
        _claude?.Dispose();
        _tts?.Dispose();
        _audioSendGate.Dispose();
    }

    private sealed record PendingAudio(byte[] Bytes, float Level);
}
