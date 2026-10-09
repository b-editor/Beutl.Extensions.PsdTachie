using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
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
    public double GetLevel(TimeSpan time, TimeSpan hold)
    {
        if (time < TimeSpan.Zero || time > Source.Duration)
            return double.NegativeInfinity;

        long last = (long)Math.Floor(time.TotalSeconds * BinsPerSecond);
        long first = Math.Max(0, last - (long)Math.Ceiling(hold.TotalSeconds * BinsPerSecond));
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
        if (!Source.Read(TimeSpan.FromSeconds(block), TimeSpan.FromSeconds(1), out Ref<IPcm>? pcmRef))
            return values;

        using (pcmRef)
        {
            IPcm pcm = pcmRef.Value;
            using Pcm<Stereo32BitFloat> stereo = pcm.Convert<Stereo32BitFloat>();
            Span<Stereo32BitFloat> samples = stereo.DataSpan;
            int samplesPerBin = Math.Max(1, stereo.SampleRate / BinsPerSecond);
            for (int i = 0; i < BinsPerSecond; i++)
            {
                int start = i * samplesPerBin;
                if (start >= samples.Length)
                    break;

                int end = Math.Min(samples.Length, start + samplesPerBin);
                double sum = 0;
                for (int s = start; s < end; s++)
                {
                    Stereo32BitFloat sample = samples[s];
                    sum += (sample.Left * (double)sample.Left + sample.Right * (double)sample.Right) / 2;
                }

                values[i] = (float)LipSync.ToDecibels(Math.Sqrt(sum / (end - start)));
            }
        }

        return values;
    }
}
