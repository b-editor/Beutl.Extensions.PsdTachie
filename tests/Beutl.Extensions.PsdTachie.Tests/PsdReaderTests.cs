using Beutl.Extensions.PsdTachie.Psd;
using SkiaSharp;

namespace Beutl.Extensions.PsdTachie.Tests;

// The fixtures are written by psd-tools (tools/make_psd_fixtures.py), an independent PSD implementation,
// and the expected images are its composites. Pixel layers carry only Shift_JIS legacy names, as files
// saved by Japanese Photoshop do; the groups also carry Unicode names.
public class PsdReaderTests
{
    private static string TestData(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);

    [TestCase("rle")]
    [TestCase("raw")]
    [TestCase("zip")]
    public void ReadsLayerTree(string compression)
    {
        PsdDocument document = PsdReader.Read(TestData($"tachie_{compression}.psd"));

        Assert.That(document.Width, Is.EqualTo(64));
        Assert.That(document.Height, Is.EqualTo(48));
        Assert.That(document.Root.Children.Select(l => l.Name),
            Is.EqualTo(new[] { "背景", "体", "目", "口", "!輪郭", "非表示", "半透明" }));
        Assert.That(document.AllLayers.Select(l => l.Path), Is.EqualTo(new[]
        {
            "背景", "体", "体/胴体", "体/影", "目", "目/*開き", "目/*半目", "目/*閉じ",
            "口", "口/*閉じ", "口/*半開き", "口/*開き", "!輪郭", "非表示", "半透明",
        }));

        PsdLayer shadow = document.FindByPath("体/影")!;
        Assert.That(shadow.IsClipped, Is.True);
        Assert.That(shadow.BlendModeKey, Is.EqualTo("mul "));
        Assert.That(shadow.Parent!.Name, Is.EqualTo("体"));
        Assert.That(document.FindByPath("目/*半目")!.IsVisibleByDefault, Is.False);
        Assert.That(document.FindByPath("目/*開き")!.IsRadio, Is.True);
        Assert.That(document.FindByPath("!輪郭")!.IsForced, Is.True);
        Assert.That(document.FindByPath("半透明")!.Opacity, Is.EqualTo(128));
        Assert.That(document.FindByPath("体/胴体")!, Has.Property(nameof(PsdLayer.Left)).EqualTo(10)
            .And.Property(nameof(PsdLayer.Top)).EqualTo(20)
            .And.Property(nameof(PsdLayer.Width)).EqualTo(20));
    }

    [TestCase("rle")]
    [TestCase("raw")]
    [TestCase("zip")]
    public void CompositeMatchesPsdTools(string compression)
    {
        PsdDocument document = PsdReader.Read(TestData($"tachie_{compression}.psd"));
        // psd-tools composites the saved visibility; the '!' rule is this package's own convention.
        bool[] flags = document.AllLayers.Select(l => l.IsVisibleByDefault).ToArray();

        using var compositor = new PsdCompositor(document);
        using SKBitmap actual = compositor.Compose(flags);
        using SKBitmap expected = SKBitmap.Decode(TestData($"tachie_{compression}_expected.png"));

        AssertSimilar(actual, expected, tolerance: 2);
    }

    [Test]
    public void Reads16BitDocumentsFromTheLr16Block()
    {
        // 16-bit layers live in a global "Lr16" block and use ZIP compression with prediction here.
        PsdDocument document = PsdReader.Read(TestData("depth16.psd"));
        Assert.That(document.AllLayers.Select(l => l.Path), Is.EqualTo(new[] { "bg", "grp", "grp/red" }));

        using var compositor = new PsdCompositor(document);
        using SKBitmap actual = compositor.Compose(document.AllLayers.Select(l => l.IsVisibleByDefault).ToArray());
        using SKBitmap expected = SKBitmap.Decode(TestData("depth16_expected.png"));
        AssertSimilar(actual, expected, tolerance: 1);
    }

    [Test]
    public void Reads32BitDocumentsAsLinearLight()
    {
        // Photoshop stores 32-bit channels as linear light, so they are encoded with the sRGB curve.
        // psd-tools writes 8-bit values divided by 255 instead, so its 200 reads back as sRGB(200/255) = 229.
        PsdDocument document = PsdReader.Read(TestData("depth32.psd"));
        Assert.That(document.AllLayers.Select(l => l.Path), Is.EqualTo(new[] { "bg", "grp", "grp/red" }));

        using var compositor = new PsdCompositor(document);
        using SKBitmap actual = compositor.Compose(document.AllLayers.Select(l => l.IsVisibleByDefault).ToArray());
        Assert.That(actual.GetPixel(1, 1), Is.EqualTo(new SKColor(229, 229, 229)));
        Assert.That(actual.GetPixel(10, 10), Is.EqualTo(new SKColor(239, 110, 110)));
    }

    [Test]
    public void ForcedLayerIsAlwaysShown()
    {
        PsdDocument document = PsdReader.Read(TestData("tachie_rle.psd"));
        bool[] flags = PsdLayerState.Resolve(document, "-!輪郭");

        Assert.That(flags[document.FindByPath("!輪郭")!.Index], Is.True);

        using var compositor = new PsdCompositor(document);
        using SKBitmap actual = compositor.Compose(flags);
        Assert.That(actual.GetPixel(0, 10), Is.EqualTo(new SKColor(0, 0, 0, 255)));
    }

    [Test]
    public void HidingGroupHidesItsChildren()
    {
        PsdDocument document = PsdReader.Read(TestData("tachie_rle.psd"));
        bool[] flags = PsdLayerState.Resolve(document, "-体");

        using var compositor = new PsdCompositor(document);
        using SKBitmap actual = compositor.Compose(flags);
        Assert.That(actual.GetPixel(20, 25), Is.EqualTo(new SKColor(200, 200, 200, 255)));
    }

    [Test]
    public void RejectsNonPsd()
    {
        Assert.Throws<InvalidDataException>(() => PsdReader.Read("not a psd file at all"u8.ToArray()));
    }

    [Test]
    public void UnpackBitsHandlesLiteralRunAndNoOp()
    {
        // 2 literal bytes, a run of 3, a no-op (-128), then 1 literal byte.
        byte[] packed = [0x01, 0xAA, 0xBB, 0xFE, 0xCC, 0x80, 0x00, 0xDD];
        var output = new byte[6];
        PsdReader.UnpackBits(packed, output);
        Assert.That(output, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC, 0xCC, 0xCC, 0xDD }));
    }

    private static void AssertSimilar(SKBitmap actual, SKBitmap expected, int tolerance)
    {
        Assert.That(actual.Width, Is.EqualTo(expected.Width));
        Assert.That(actual.Height, Is.EqualTo(expected.Height));

        var mismatches = new List<string>();
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                SKColor a = actual.GetPixel(x, y);
                SKColor e = expected.GetPixel(x, y);
                if (Math.Abs(a.Red - e.Red) > tolerance || Math.Abs(a.Green - e.Green) > tolerance
                    || Math.Abs(a.Blue - e.Blue) > tolerance || Math.Abs(a.Alpha - e.Alpha) > tolerance)
                {
                    mismatches.Add($"({x},{y}) actual={a} expected={e}");
                }
            }
        }

        Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches.Take(10)));
    }
}
