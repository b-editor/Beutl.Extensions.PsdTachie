using System.Text.Json.Nodes;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Extensions.PsdTachie.Tests;

// Renders the drawable through Beutl's own scene pipeline. The 64x48 fixture is centred in a 1920x1080
// scene, so a PSD pixel (x, y) lands on scene pixel (928 + x, 516 + y). Bitmaps are drawn with the same
// Mitchell filter as Beutl's own images, which blends about 1/18 of each neighbour into edge pixels, so
// exact colours are checked away from layer edges.
public class PsdTachieRenderTests
{
    private const int OriginX = (1920 - 64) / 2;
    private const int OriginY = (1080 - 48) / 2;

    private static readonly (byte, byte, byte) s_background = (200, 200, 200);

    private static string TestData(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);

    private static PsdTachieDrawable CreateDrawable()
    {
        var drawable = new PsdTachieDrawable();
        drawable.Source.Value = new FileInfo(TestData("tachie_rle.psd"));
        drawable.MouthClosed.Value = "口/*閉じ";
        drawable.MouthHalfOpen.Value = "口/*半開き";
        drawable.MouthOpen.Value = "口/*開き";
        drawable.EyeOpen.Value = "目/*開き";
        drawable.EyeHalfOpen.Value = "目/*半目";
        drawable.EyeClosed.Value = "目/*閉じ";
        drawable.BlinkInterval.Value = 1f;
        drawable.BlinkRandomness.Value = 0f;
        drawable.BlinkDuration.Value = 0.3f;
        return drawable;
    }

    private static (byte R, byte G, byte B, byte A) PsdPixel(Bitmap frame, int x, int y) =>
        HeadlessScene.GetPixel(frame, OriginX + x, OriginY + y);

    [Test]
    public void RendersCompositedLayersAtTheirPsdPositions()
    {
        Scene scene = HeadlessScene.Create((CreateDrawable(), MediaTime.FromSeconds(5)));

        using Bitmap frame = HeadlessScene.Render(scene, MediaTime.FromSeconds(0.5));
        HeadlessScene.SaveForInspection(frame, "psd-default.png");

        HeadlessScene.AssertColor(PsdPixel(frame, 1, 1), s_background);
        HeadlessScene.AssertColor(PsdPixel(frame, 20, 25), (220, 40, 40));
        // The clipped multiply shadow only darkens the body.
        HeadlessScene.AssertColor(PsdPixel(frame, 20, 35), (110, 20, 40), tolerance: 6);
        // '!' layers are shown even though the PSD saved them hidden.
        HeadlessScene.AssertColor(PsdPixel(frame, 0, 10), (0, 0, 0));
        // Outside the PSD the scene stays transparent.
        Assert.That(HeadlessScene.GetPixel(frame, 100, 100).A, Is.EqualTo(0));
    }

    [Test]
    public void EyesBlinkOnTheConfiguredInterval()
    {
        Scene scene = HeadlessScene.Create((CreateDrawable(), MediaTime.FromSeconds(5)));

        using Bitmap open = HeadlessScene.Render(scene, MediaTime.FromSeconds(0.5));
        using Bitmap closed = HeadlessScene.Render(scene, MediaTime.FromSeconds(1.15));
        HeadlessScene.SaveForInspection(closed, "psd-blink.png");

        // The open eye covers rows 4-7; the closed eye is the single row 6.
        HeadlessScene.AssertColor(PsdPixel(open, 44, 5), (0, 160, 0));
        HeadlessScene.AssertColor(PsdPixel(closed, 44, 4), s_background, tolerance: 12);
        (byte r, byte g, byte b, _) = PsdPixel(closed, 44, 6);
        Assert.That(g, Is.GreaterThan(r + 40).And.GreaterThan(b + 40), "closed eye line");
    }

    [Test]
    public void MouthFollowsTheAudioLevel()
    {
        TestWaveDecoder.EnsureRegistered();
        string wav = Path.Combine(TestContext.CurrentContext.WorkDirectory, "lipsync.wav");
        const int rate = 44100;
        // One loud second, then one silent second.
        TestWaveDecoder.Write(wav, rate, Enumerable.Range(0, rate * 2)
            .Select(i => i < rate ? 0.5f * MathF.Sin(2 * MathF.PI * 220 * i / rate) : 0f));

        PsdTachieDrawable drawable = CreateDrawable();
        drawable.EyeOpen.Value = "";
        drawable.EyeHalfOpen.Value = "";
        drawable.EyeClosed.Value = "";
        var audio = new SoundSource();
        audio.ReadFrom(new Uri(wav));
        drawable.LipSyncAudio.Value = audio;
        Scene scene = HeadlessScene.Create((drawable, MediaTime.FromSeconds(5)));

        using Bitmap speaking = HeadlessScene.Render(scene, MediaTime.FromSeconds(0.5));
        using Bitmap silent = HeadlessScene.Render(scene, MediaTime.FromSeconds(1.5));
        HeadlessScene.SaveForInspection(speaking, "psd-lipsync-open.png");

        // The open mouth covers rows 12-16; the closed one is the single row 14.
        HeadlessScene.AssertColor(PsdPixel(speaking, 43, 13), (190, 0, 0));
        HeadlessScene.AssertColor(PsdPixel(silent, 43, 12), s_background, tolerance: 12);
        (byte r, byte g, byte b, _) = PsdPixel(silent, 43, 14);
        Assert.That(r, Is.GreaterThan(g + 40).And.GreaterThan(b + 40), "closed mouth line");
    }

    [Test]
    public void StoredLayerStateHidesAndShowsLayers()
    {
        PsdTachieDrawable drawable = CreateDrawable();
        drawable.MouthClosed.Value = "";
        drawable.MouthHalfOpen.Value = "";
        drawable.MouthOpen.Value = "";
        drawable.Layers.Value = "-体\n+非表示";
        Scene scene = HeadlessScene.Create((drawable, MediaTime.FromSeconds(5)));

        using Bitmap frame = HeadlessScene.Render(scene, MediaTime.FromSeconds(0.5));

        HeadlessScene.AssertColor(PsdPixel(frame, 20, 25), s_background);
        HeadlessScene.AssertColor(PsdPixel(frame, 52, 32), (255, 255, 0));
    }

    [Test]
    public void SurvivesSerializationRoundTrip()
    {
        var element = new Element { Length = { Value = MediaTime.FromSeconds(5) } };
        element.AddObject(CreateDrawable());

        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        var drawable = (PsdTachieDrawable)restored.Objects.Single();
        Assert.That(drawable.Source.Value!.FullName, Is.EqualTo(Path.GetFullPath(TestData("tachie_rle.psd"))));
        Assert.That(drawable.MouthOpen.Value, Is.EqualTo("口/*開き"));
        Assert.That(drawable.BlinkInterval.Value, Is.EqualTo(1f));
    }

    [Test]
    public void TheBackHalfOfASplitKeepsTheMouthOnTheVoice()
    {
        TestWaveDecoder.EnsureRegistered();
        string wav = Path.Combine(TestContext.CurrentContext.WorkDirectory, "lipsync-split.wav");
        const int rate = 44100;
        // One loud second, then one silent second.
        TestWaveDecoder.Write(wav, rate, Enumerable.Range(0, rate * 2)
            .Select(i => i < rate ? 0.5f * MathF.Sin(2 * MathF.PI * 220 * i / rate) : 0f));

        PsdTachieDrawable Create()
        {
            // Without eyes: blinks start over in the back half.
            PsdTachieDrawable drawable = CreateDrawable();
            drawable.EyeOpen.Value = "";
            drawable.EyeHalfOpen.Value = "";
            drawable.EyeClosed.Value = "";
            var audio = new SoundSource();
            audio.ReadFrom(new Uri(wav));
            drawable.LipSyncAudio.Value = audio;
            return drawable;
        }

        Scene whole = HeadlessScene.Create((Create(), MediaTime.FromSeconds(5)));
        Scene split = HeadlessScene.Create((Create(), MediaTime.FromSeconds(5)));
        Element back = HeadlessScene.Split(split, split.Children[0], MediaTime.FromSeconds(0.5));

        var backDrawable = back.Objects.OfType<PsdTachieDrawable>().Single();
        Assert.That(backDrawable.LipSyncOffset.Value, Is.EqualTo(MediaTime.FromSeconds(-0.5)));
        Assert.That(backDrawable.TryGetOriginalDuration(out MediaTime backDuration), Is.True);
        Assert.That(backDuration, Is.EqualTo(MediaTime.FromSeconds(1.5)), "The rest of the voice.");

        // Still speaking, then silent: the back half must not start the voice over.
        foreach (double seconds in new[] { 0.75, 1.25 })
        {
            using Bitmap expected = HeadlessScene.Render(whole, MediaTime.FromSeconds(seconds));
            using Bitmap actual = HeadlessScene.Render(split, MediaTime.FromSeconds(seconds));
            HeadlessScene.AssertSameFrame(expected, actual);
        }
    }
}
