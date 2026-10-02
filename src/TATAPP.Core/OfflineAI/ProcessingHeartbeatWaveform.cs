namespace TATAPP.Core.OfflineAI;

/// <summary>
/// Defines the quiet, deterministic processing pulse shared by desktop and Android.
/// Platform players own scheduling and lifecycle; this type owns only waveform identity.
/// </summary>
public static class ProcessingHeartbeatWaveform
{
    public const int SampleRate = 22_050;
    public const int DurationMilliseconds = 420;
    public const int FirstPulseDelayMilliseconds = 900;
    public const int PulseCadenceMilliseconds = 4_000;
    public const double MaximumCadenceGain = 2.5;
    public const double FirstToneFrequency = 620;
    public const double SecondToneFrequency = 760;

    public static short[] CreatePcm16()
    {
        var samples = new short[SampleRate * DurationMilliseconds / 1000];
        for (var index = 0; index < samples.Length; index++)
        {
            var milliseconds = index * 1000d / SampleRate;
            var sample = Tone(milliseconds, 45, 72, FirstToneFrequency, 0.10) +
                         Tone(milliseconds, 205, 86, SecondToneFrequency, 0.085);
            samples[index] = (short)Math.Round(sample * short.MaxValue);
        }
        return samples;
    }

    public static byte[] CreateWaveFile()
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var samples = CreatePcm16();
        var dataLength = samples.Length * sizeof(short);
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }

    public static short[] CreateCadenceBuffer(double gain)
    {
        if (!double.IsFinite(gain) || gain <= 0 || gain > MaximumCadenceGain)
            throw new ArgumentOutOfRangeException(nameof(gain));

        var pulse = CreatePcm16();
        var cadence = new short[SampleRate * PulseCadenceMilliseconds / 1000];
        var pulseOffset = SampleRate * FirstPulseDelayMilliseconds / 1000;
        if (pulseOffset + pulse.Length > cadence.Length)
            throw new InvalidOperationException("The processing pulse does not fit inside its cadence buffer.");

        for (var index = 0; index < pulse.Length; index++)
        {
            var amplified = Math.Round(pulse[index] * gain);
            cadence[pulseOffset + index] = (short)Math.Clamp(amplified, short.MinValue, short.MaxValue);
        }
        return cadence;
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
