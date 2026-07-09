using NAudio.Wave;

namespace Clicky.Windows.Services;

public sealed class MicrophoneCaptureService : IDisposable
{
    private WaveInEvent? _capture;

    public event Action<ReadOnlyMemory<byte>, float>? AudioAvailable;
    public event Action<Exception>? CaptureFailed;

    public bool IsRecording => _capture is not null;

    public void Start()
    {
        if (_capture is not null)
        {
            return;
        }

        var capture = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16_000, 16, 1),
            BufferMilliseconds = 50,
            NumberOfBuffers = 3
        };
        capture.DataAvailable += HandleDataAvailable;
        capture.RecordingStopped += HandleRecordingStopped;

        try
        {
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception exception)
        {
            capture.Dispose();
            CaptureFailed?.Invoke(exception);
        }
    }

    public void Stop()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.StopRecording();
        }
        catch (Exception)
        {
            capture.Dispose();
        }
    }

    private void HandleDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var pcm = eventArgs.Buffer.AsMemory(0, eventArgs.BytesRecorded).ToArray();
        var peak = 0f;
        for (var offset = 0; offset + 1 < pcm.Length; offset += 2)
        {
            var sample = BitConverter.ToInt16(pcm, offset);
            peak = Math.Max(peak, Math.Abs(sample) / (float)short.MaxValue);
        }

        AudioAvailable?.Invoke(pcm, peak);
    }

    private void HandleRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (sender is WaveInEvent capture)
        {
            capture.DataAvailable -= HandleDataAvailable;
            capture.RecordingStopped -= HandleRecordingStopped;
            capture.Dispose();
        }

        if (eventArgs.Exception is not null)
        {
            CaptureFailed?.Invoke(eventArgs.Exception);
        }
    }

    public void Dispose() => Stop();
}
