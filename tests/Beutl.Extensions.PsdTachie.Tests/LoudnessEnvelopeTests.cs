using Beutl.Composition;
using Beutl.Extensions.PsdTachie.Animation;
using Beutl.Media.Source;

namespace Beutl.Extensions.PsdTachie.Tests;

public class LoudnessEnvelopeTests
{
    private const double SurroundGain = 0.70710677;

    [TestCase(44100, 1)]
    [TestCase(44100, 2)]
    [TestCase(48000, 1)]
    [TestCase(48000, 2)]
    public void KeepsChannelLevelsHoldAndRandomAccess(int rate, int channels)
    {
        TestWaveDecoder.EnsureRegistered();
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"levels-{rate}-{channels}.wav");
        TestWaveDecoder.Write(path, rate, Enumerable.Range(0, rate * 2 + rate / 50)
            .SelectMany(i => Enumerable.Range(0, channels).Select(c => i < rate && c == 0 ? 0.5f : 0f)), channels);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        using SoundSource.Resource resource = source.ToResource(CompositionContext.Default);
        var envelope = new LoudnessEnvelope(resource);
        MediaTime hold = MediaTime.FromSeconds(0.04);
        double expected = LipSync.ToDecibels(0.5 / Math.Sqrt(channels));

        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(1.5), hold), Is.EqualTo(double.NegativeInfinity));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(0.5), hold), Is.EqualTo(expected).Within(0.001));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(1.025), hold), Is.EqualTo(expected).Within(0.001));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(1.06), hold), Is.EqualTo(double.NegativeInfinity));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(2.01), hold), Is.EqualTo(double.NegativeInfinity));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(-0.01), hold), Is.EqualTo(double.NegativeInfinity));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(3), hold), Is.EqualTo(double.NegativeInfinity));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(0.25), hold), Is.EqualTo(expected).Within(0.001));
    }

    // Decoders return the source's own channels, while the thresholds mean the level of the stereo fold. The gains
    // are what the one active channel adds to the left and right sides.
    [TestCase(6, 0, 1.0, 0.0)] // 5.1 front left
    [TestCase(6, 2, SurroundGain, SurroundGain)] // 5.1 centre
    [TestCase(6, 3, 0.0, 0.0)] // 5.1 LFE
    [TestCase(6, 5, 0.0, SurroundGain)] // 5.1 back right
    [TestCase(8, 2, SurroundGain, SurroundGain)] // 7.1 centre
    [TestCase(8, 6, SurroundGain, 0.0)] // 7.1 side left
    public void MeasuresASurroundSourceAsItsStereoFold(int channels, int active, double left, double right)
    {
        TestWaveDecoder.EnsureRegistered();
        const int rate = 48000;
        const double amplitude = 0.5;
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"surround-{channels}-{active}.wav");
        TestWaveDecoder.Write(path, rate, Enumerable.Range(0, rate)
            .SelectMany(_ => Enumerable.Range(0, channels).Select(c => c == active ? (float)amplitude : 0f)), channels);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        using SoundSource.Resource resource = source.ToResource(CompositionContext.Default);
        var envelope = new LoudnessEnvelope(resource);

        MediaTime hold = MediaTime.FromSeconds(0.04);
        double expected = LipSync.ToDecibels(amplitude * Math.Sqrt((left * left + right * right) / 2));

        Assert.That(resource.ChannelLayout.ChannelCount, Is.EqualTo(channels));
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(0.5), hold), Is.EqualTo(expected).Within(0.01));
        // The last moment of the file holds on to the bins before it across an empty read.
        Assert.That(envelope.GetLevel(MediaTime.FromSeconds(1), hold), Is.EqualTo(expected).Within(0.01));
    }
}
