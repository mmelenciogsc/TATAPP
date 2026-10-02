using System.IO;
using System.Media;
using TATAPP.Core.OfflineAI;

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
            timer = new Timer(PlayPulse, null,
                TimeSpan.FromMilliseconds(1200),
                TimeSpan.FromMilliseconds(ProcessingHeartbeatWaveform.PulseCadenceMilliseconds));
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

    internal static byte[] CreateWaveData() => ProcessingHeartbeatWaveform.CreateWaveFile();
}
