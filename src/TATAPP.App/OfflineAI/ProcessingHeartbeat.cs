using System.IO;
using System.Media;

namespace TATAPP.App.OfflineAI;

/// <summary>
/// A quiet two-note pulse that confirms long local-model work is still progressing without
/// repeatedly interrupting a screenreader's speech.
/// </summary>
internal sealed class ProcessingHeartbeat : IDisposable
{
    private readonly object synchronization = new();
    private MemoryStream? waveStream;
    private SoundPlayer? player;
    private Timer? timer;
    private bool disposed;

    private ProcessingHeartbeat()
    {
        try
        {
            waveStream = new MemoryStream(CreateWaveData(), writable: false);
            player = new SoundPlayer(waveStream);
            player.Load();
            timer = new Timer(PlayPulse, null, TimeSpan.FromMilliseconds(1200), TimeSpan.FromSeconds(4));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            ReleaseAudio();
        }
    }

    public static ProcessingHeartbeat Start() => new();

    private void PlayPulse(object? state)
    {
        lock (synchronization)
        {
            if (disposed || player is null) return;
            try
            {
                player.Play();
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                timer?.Dispose();
                timer = null;
            }
        }
    }

    public void Dispose()
    {
        lock (synchronization)
        {
            if (disposed) return;
            disposed = true;
            timer?.Dispose();
            timer = null;
            ReleaseAudio();
        }
    }

    private void ReleaseAudio()
    {
        if (player is not null)
        {
            try
            {
                player.Stop();
                player.Dispose();
            }
            catch (InvalidOperationException)
            {
            }
            player = null;
        }
        waveStream?.Dispose();
        waveStream = null;
    }

    internal static byte[] CreateWaveData()
    {
        const int sampleRate = 22050;
        const int durationMilliseconds = 420;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = sampleRate * durationMilliseconds / 1000;
        var dataLength = sampleCount * channels * bitsPerSample / 8;

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        for (var index = 0; index < sampleCount; index++)
        {
            var milliseconds = index * 1000d / sampleRate;
            var sample = Tone(milliseconds, 45, 72, 620, 0.10) +
                         Tone(milliseconds, 205, 86, 760, 0.085);
            writer.Write((short)Math.Round(sample * short.MaxValue));
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static double Tone(double currentMilliseconds, double startMilliseconds,
        double durationMilliseconds, double frequency, double amplitude)
    {
        var elapsed = currentMilliseconds - startMilliseconds;
        if (elapsed < 0 || elapsed >= durationMilliseconds) return 0;
        var envelope = Math.Sin(Math.PI * elapsed / durationMilliseconds);
        envelope *= envelope;
        return amplitude * envelope * Math.Sin(2 * Math.PI * frequency * elapsed / 1000);
    }
}
