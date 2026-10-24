using Beutl.Audio;
using Beutl.Media.Source;

namespace Beutl.Extensions.PsdTachie.Animation;

public enum MouthShape
{
    Closed,
    HalfOpen,
    Open,
}

public static class LipSync
{
    public static MouthShape GetShape(double levelDb, double halfOpenThresholdDb, double openThresholdDb)
    {
        if (levelDb >= openThresholdDb)
            return MouthShape.Open;
        return levelDb >= halfOpenThresholdDb ? MouthShape.HalfOpen : MouthShape.Closed;
    }

    public static double ToDecibels(double rms)
    {
        return rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity;
    }
}

/// <summary>
/// The loudness of an audio source in 10 ms bins, decoded one second at a time as playback reaches it.
/// </summary>
internal sealed class LoudnessEnvelope
{
    public const int BinsPerSecond = 100;

    private readonly Dictionary<int, float[]> _blocks = [];

    public LoudnessEnvelope(SoundSource.Resource source)
    {
        Source = source;
    }

    public SoundSource.Resource Source { get; }

    /// <summary>The loudest bin within <paramref name="hold"/> before <paramref name="time"/>, in dBFS.</summary>
    public double GetLevel(MediaTime time, MediaTime hold)
    {
        if (time < MediaTime.Zero || time > Source.Duration)
            return double.NegativeInfinity;

        long last = time.ToSamples(BinsPerSecond, MidpointRounding.ToNegativeInfinity);
        long first = Math.Max(0, last - hold.ToSamples(BinsPerSecond, MidpointRounding.ToPositiveInfinity));
        double peak = double.NegativeInfinity;
        for (long bin = first; bin <= last; bin++)
        {
            peak = Math.Max(peak, GetBin(bin));
        }

        return peak;
    }

    private double GetBin(long bin)
    {
        int block = (int)(bin / BinsPerSecond);
        if (!_blocks.TryGetValue(block, out float[]? values))
        {
            values = Decode(block);
            _blocks[block] = values;
        }

        return values[(int)(bin % BinsPerSecond)];
    }

    private float[] Decode(int block)
    {
        var values = new float[BinsPerSecond];
        values.AsSpan().Fill(float.NegativeInfinity);
        if (!Source.Read(MediaTime.FromSeconds(block), MediaTime.FromSeconds(1), out AudioBuffer? audio))
            return values;

        using (audio)
        {
            int samplesPerBin = Math.Max(1, audio.SampleRate / BinsPerSecond);
            for (int i = 0; i < BinsPerSecond; i++)
            {
                int start = i * samplesPerBin;
                if (start >= audio.SampleCount)
                    break;

                int end = Math.Min(audio.SampleCount, start + samplesPerBin);
                double sum = 0;
                for (int channel = 0; channel < audio.ChannelCount; channel++)
                {
                    Span<float> samples = audio.GetChannelData(channel);
                    for (int s = start; s < end; s++)
                    {
                        sum += samples[s] * (double)samples[s];
                    }
                }

                values[i] = (float)LipSync.ToDecibels(Math.Sqrt(sum / ((end - start) * (double)audio.ChannelCount)));
            }
        }

        return values;
    }
}
