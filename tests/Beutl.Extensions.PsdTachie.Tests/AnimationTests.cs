using Beutl.Extensions.PsdTachie.Animation;

namespace Beutl.Extensions.PsdTachie.Tests;

public class AnimationTests
{
    [TestCase(-60.0, MouthShape.Closed)]
    [TestCase(-36.0, MouthShape.HalfOpen)]
    [TestCase(-30.0, MouthShape.HalfOpen)]
    [TestCase(-24.0, MouthShape.Open)]
    [TestCase(double.NegativeInfinity, MouthShape.Closed)]
    public void MouthFollowsThresholds(double level, MouthShape expected)
    {
        Assert.That(LipSync.GetShape(level, -36, -24), Is.EqualTo(expected));
    }

    [Test]
    public void DecibelsOfFullScaleIsZero()
    {
        Assert.That(LipSync.ToDecibels(1), Is.EqualTo(0).Within(1e-9));
        Assert.That(LipSync.ToDecibels(0.5), Is.EqualTo(-6.0206).Within(1e-3));
    }

    [Test]
    public void BlinkWithoutRandomnessHappensAtEveryInterval()
    {
        TimeSpan interval = TimeSpan.FromSeconds(4);
        TimeSpan duration = TimeSpan.FromMilliseconds(150);

        Assert.That(Shape(3.9), Is.EqualTo(EyeShape.Open));
        Assert.That(Shape(4.01), Is.EqualTo(EyeShape.HalfOpen));
        Assert.That(Shape(4.075), Is.EqualTo(EyeShape.Closed));
        Assert.That(Shape(4.14), Is.EqualTo(EyeShape.HalfOpen));
        Assert.That(Shape(4.2), Is.EqualTo(EyeShape.Open));
        Assert.That(Shape(8.075), Is.EqualTo(EyeShape.Closed));

        EyeShape Shape(double seconds) =>
            BlinkSchedule.GetShape(TimeSpan.FromSeconds(seconds), interval, 0, duration, seed: 0);
    }

    [Test]
    public void RandomBlinksAreRepeatableAndDependOnSeed()
    {
        EyeShape[] Sample(int seed) => Enumerable.Range(0, 3000)
            .Select(i => BlinkSchedule.GetShape(TimeSpan.FromSeconds(i / 30.0), TimeSpan.FromSeconds(4), 0.5,
                TimeSpan.FromMilliseconds(150), seed))
            .ToArray();

        Assert.That(Sample(1), Is.EqualTo(Sample(1)));
        Assert.That(Sample(1), Is.Not.EqualTo(Sample(2)));
        Assert.That(Sample(1).Count(s => s == EyeShape.Closed), Is.GreaterThan(0));
    }

    [Test]
    public void NegativeTimeKeepsEyesOpen()
    {
        Assert.That(BlinkSchedule.GetShape(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(4), 0.5,
            TimeSpan.FromMilliseconds(150), 0), Is.EqualTo(EyeShape.Open));
    }
}
