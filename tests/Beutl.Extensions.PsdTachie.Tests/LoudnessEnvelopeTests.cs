using Beutl.Composition;
using Beutl.Extensions.PsdTachie.Animation;
using Beutl.Media.Source;

namespace Beutl.Extensions.PsdTachie.Tests;

public class LoudnessEnvelopeTests
{
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
}
