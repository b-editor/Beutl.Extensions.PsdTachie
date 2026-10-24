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
        drawable.LipSyncAudio.Value = WriteSilence(2);
        drawable.LipSyncOffset.Value = MediaTime.FromSeconds(0.5);
        var element = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        element.AddObject(drawable);

        Assert.That(element.HasOriginalDuration(), Is.True);
        Assert.That(element.TryGetOriginalDuration(out MediaTime duration), Is.True);
        Assert.That(duration, Is.EqualTo(MediaTime.FromSeconds(2.5)));
    }

    [Test]
    public void HasNoOriginalDurationWithoutAudio()
    {
        var drawable = new PsdTachieDrawable();

        Assert.That(drawable.HasOriginalDuration(), Is.False);
        Assert.That(drawable.TryGetOriginalDuration(out _), Is.False);
    }
}
