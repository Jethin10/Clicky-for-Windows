using NAudio.Wave;

namespace Clicky.Windows.Services;

public static class PcmWaveEncoder
{
    private static readonly WaveFormat CaptureFormat = new(16_000, 16, 1);

    public static byte[] Encode16KhzMono(byte[] pcm16)
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, CaptureFormat))
        {
            writer.Write(pcm16, 0, pcm16.Length);
            writer.Flush();
        }

        return stream.ToArray();
    }
}
