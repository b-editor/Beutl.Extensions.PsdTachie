using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Extensions.PsdTachie.Tests;

// The timeline's "元の長さに変更" and the clamp-to-original-length setting read these values through Element.
public class OriginalDurationTests
{
    private static SoundSource WriteSilence(double seconds)
    {
        TestWaveDecoder.EnsureRegistered();
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"voice-{Guid.NewGuid():N}.wav");
        TestWaveDecoder.Write(path, 44100, Enumerable.Repeat(0f, (int)(44100 * seconds)));
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        return source;
    }

    [Test]
    public void FollowsTheLipSyncAudioAndItsOffset()
    {
        var drawable = new PsdTachieDrawable();
        drawable.LipSyncAudio.CurrentValue = WriteSilence(2);
        drawable.LipSyncOffset.CurrentValue = TimeSpan.FromSeconds(0.5);
        var element = new Element { Length = TimeSpan.FromSeconds(10) };
        element.AddObject(drawable);

        Assert.That(element.HasOriginalDuration(), Is.True);
        Assert.That(element.TryGetOriginalDuration(out TimeSpan duration), Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromSeconds(2.5)).Within(TimeSpan.FromMilliseconds(1)));
    }

    [Test]
    public void HasNoOriginalDurationWithoutAudio()
    {
        var drawable = new PsdTachieDrawable();

        Assert.That(drawable.HasOriginalDuration(), Is.False);
        Assert.That(drawable.TryGetOriginalDuration(out _), Is.False);
    }
}
