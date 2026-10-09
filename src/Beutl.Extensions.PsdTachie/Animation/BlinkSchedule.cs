namespace Beutl.Extensions.PsdTachie.Animation;

public enum EyeShape
{
    Open,
    HalfOpen,
    Closed,
}

/// <summary>
/// A deterministic blink timeline: the eye shape depends only on the time and the parameters, so any frame
/// renders the same way whether it is reached by playback, seeking or export.
/// </summary>
public static class BlinkSchedule
{
    // Rendering an hour at a four-second interval needs about 900 steps; the cap only guards bad input.
    private const int MaxBlinks = 1_000_000;

    /// <param name="time">The time since the object started.</param>
    /// <param name="interval">The average time between blinks.</param>
    /// <param name="randomness">How much each interval may deviate from the average, from 0 to 1.</param>
    /// <param name="duration">How long one blink takes.</param>
    /// <param name="seed">Selects a different but repeatable sequence of intervals.</param>
    public static EyeShape GetShape(TimeSpan time, TimeSpan interval, double randomness, TimeSpan duration, int seed)
    {
        double t = time.TotalSeconds;
        double average = interval.TotalSeconds;
        double length = duration.TotalSeconds;
        if (t < 0 || average <= 0 || length <= 0)
            return EyeShape.Open;

        randomness = Math.Clamp(randomness, 0, 1);
        double minimum = Math.Max(length * 2, average * 0.05);
        double blinkStart = 0;
        for (int k = 0; k < MaxBlinks; k++)
        {
            double jitter = (Random01(seed, k) * 2 - 1) * randomness;
            blinkStart += Math.Max(minimum, average * (1 + jitter));
            if (t < blinkStart)
                return EyeShape.Open;

            if (t < blinkStart + length)
            {
                double progress = (t - blinkStart) / length;
                return progress < 1.0 / 3 || progress >= 2.0 / 3 ? EyeShape.HalfOpen : EyeShape.Closed;
            }
        }

        return EyeShape.Open;
    }

    private static double Random01(int seed, int index)
    {
        // SplitMix64 keyed by the seed and the blink index.
        ulong z = unchecked(((ulong)(uint)seed << 32 | (uint)index) + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }
}
