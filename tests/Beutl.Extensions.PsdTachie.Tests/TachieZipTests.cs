using System.IO.Compression;
using System.Reflection;
using System.Text;
using Beutl.Composition;
using Beutl.Extensions.PsdTachie.Graphics;
using Beutl.Extensions.PsdTachie.Psd;
using SkiaSharp;

namespace Beutl.Extensions.PsdTachie.Tests;

public class TachieZipTests
{
    [TestCase(false, "", false)]
    [TestCase(true, "キャラ素材/れいむ/", false)]
    [TestCase(false, "素材/", true)]
    public void OpensPartFoldersThroughTheDocumentCache(bool shiftJis, string prefix, bool unflaggedUtf8)
    {
        using var file = new PartArchive(shiftJis, prefix, unflaggedUtf8);
        LoadedPsd loaded = PsdDocumentCache.Load(file.Path);
        PsdDocument doc = loaded.Document;
        Assert.That(PsdDocumentCache.Load(file.Path), Is.SameAs(loaded));
        Assert.That((doc.Width, doc.Height), Is.EqualTo((8, 6)));
        Assert.That(doc.Root.Children.Select(g => g.Name), Is.EqualTo(new[] { "後", "体", "顔", "眉", "目", "口", "全" }));

        bool[] flags = PsdLayerState.Resolve(doc, "");
        using SKBitmap initial = loaded.Compositor.Compose(flags);
        Assert.That(initial.GetPixel(0, 0).Alpha, Is.Zero, "Optional accessories start hidden.");
        Assert.That(initial.GetPixel(2, 1), Is.EqualTo(SKColors.Red), "The 4x4 body is centred on the 8x6 canvas.");
        Assert.That(initial.GetPixel(3, 2), Is.EqualTo(SKColors.Green));
        Assert.That(initial.GetPixel(4, 3), Is.EqualTo(SKColors.Yellow));

        Assert.That(doc.Zip!.Presets.Keys, Is.EqualTo(new[] { "表情01" }));
        string state = doc.Zip.ApplyPreset(doc, "+後/01u1\n-目\n+全/*200", doc.Zip.Presets["表情01"]);
        bool[] changed = PsdLayerState.Resolve(doc, state);
        Assert.That(changed[doc.FindByPath("後/01u1")!.Index], Is.True, "Keep accessories outside the set.");
        Assert.That(changed[doc.FindByPath("全/*200")!.Index], Is.False);
        Assert.That(changed[doc.FindByPath("顔/*02b")!.Index], Is.True);
        Assert.That(changed[doc.FindByPath("目/*07-1")!.Index], Is.True);
        using SKBitmap preset = loaded.Compositor.Compose(changed);
        Assert.That(preset.GetPixel(3, 2), Is.EqualTo(new SKColor(0, 0, 128)), "The b face multiplies over the selected eye.");
        Assert.That(preset.GetPixel(4, 2), Is.EqualTo(new SKColor(128, 128, 0)), "The eye moves the brow up one pixel.");

        PsdLayerState.SetVisible(changed, doc.FindByPath("全/*200")!, true);
        using SKBitmap whole = loaded.Compositor.Compose(changed);
        Assert.That(whole.GetPixel(2, 1), Is.EqualTo(SKColors.Magenta));
        Assert.That(whole.GetPixel(3, 2).Alpha, Is.Zero, "A complete pose replaces all parts, including at transparent pixels.");
    }

    [Test]
    public void ReleasesHiddenZipBitmapsAndReusesPartsThatStayVisible()
    {
        using var file = new PartArchive();
        PsdDocument doc = PsdDocumentCache.Load(file.Path).Document;
        using var compositor = new PsdCompositor(doc);
        bool[] flags = PsdLayerState.Resolve(doc, "");
        using SKBitmap initial = compositor.Compose(flags);
        var cache = (IReadOnlyDictionary<int, SKBitmap?>)typeof(PsdCompositor)
            .GetField("_layerBitmaps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compositor)!;
        int bodyIndex = doc.FindByPath("体/*00")!.Index;
        SKBitmap body = cache[bodyIndex]!;
        SKBitmap eye = cache[doc.FindByPath("目/*00")!.Index]!;

        PsdLayerState.SetVisible(flags, doc.FindByPath("目/*07-1")!, true);
        using SKBitmap expression = compositor.Compose(flags);
        Assert.That(eye.Handle, Is.EqualTo(IntPtr.Zero));
        Assert.That(cache[bodyIndex], Is.SameAs(body));

        PsdLayer pose = doc.FindByPath("全/*200")!;
        PsdLayerState.SetVisible(flags, pose, true);
        using SKBitmap whole = compositor.Compose(flags);
        Assert.That(cache.Keys, Is.EquivalentTo(new[] { pose.Index }));
        Assert.That(body.Handle, Is.EqualTo(IntPtr.Zero));

        PsdLayerState.SetVisible(flags, pose, false);
        using SKBitmap restored = compositor.Compose(flags);
        Assert.That(restored.GetPixelSpan().SequenceEqual(expression.GetPixelSpan()), Is.True);
    }

    [Test]
    public void AutomaticFramesFollowTheSelectedBasePart()
    {
        using var file = new PartArchive();
        PsdDocument doc = PsdDocumentCache.Load(file.Path).Document;
        bool[] flags = PsdLayerState.Resolve(doc, "");
        var frames = doc.Zip!.GetAnimation("口", flags);
        Assert.That((frames.Start?.DisplayName, frames.Middle?.DisplayName, frames.End?.DisplayName),
            Is.EqualTo(("00", "00b", "00c")));
        PsdLayerState.SetVisible(flags, frames.Middle!, true);
        Assert.That(doc.Zip.GetAnimation("口", flags).Start, Is.Null, "An explicitly selected frame stays still.");
        PsdLayerState.SetVisible(flags, doc.FindByPath("目/*07-1")!, true);
        frames = doc.Zip.GetAnimation("目", flags);
        Assert.That(frames.End?.DisplayName, Is.EqualTo("07a-1"));
    }

    [Test]
    public void DrawableBlinksWithTheSelectedZipExpressionAndHonoursManualAssignments()
    {
        using var file = new PartArchive();
        var drawable = new PsdTachieDrawable();
        drawable.Source.Value = new FileInfo(file.Path);
        drawable.Layers.Value = "-目/*00\n+目/*07-1";
        drawable.BlinkInterval.Value = 1f;
        drawable.BlinkRandomness.Value = 0f;
        drawable.BlinkDuration.Value = 0.3f;

        void AssertEye(double seconds, (byte, byte, byte) color)
        {
            using var resource = drawable.ToResource(new CompositionContext(MediaTime.FromSeconds(seconds)));
            HeadlessScene.AssertColor(HeadlessScene.GetPixel(resource.Frame!, 3, 2), color, tolerance: 0);
        }

        AssertEye(0.5, (0, 0, 255));
        AssertEye(1.15, (0, 0, 0));
        drawable.EyeClosed.Value = "目/*00";
        AssertEye(1.15, (0, 128, 0));
    }

    [TestCase("cover.png")]
    [TestCase("体/00.png")]
    public void InvalidArchivesGiveTheEditorAReadError(string entryName)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                zip.CreateEntry(entryName);
            Assert.That(() => PsdDocumentCache.Load(path), Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

// Tiny hand-painted parts exercise the format without redistributing a character artist's files.
internal sealed class PartArchive : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.ZIP");

    public PartArchive(bool shiftJis = false, string prefix = "", bool unflaggedUtf8 = false)
    {
        Encoding encoding = shiftJis ? CodePagesEncodingProvider.Instance.GetEncoding(932)! : Encoding.UTF8;
        using var zip = ZipFile.Open(Path, ZipArchiveMode.Create, unflaggedUtf8 ? Encoding.Latin1 : encoding);
        ZipArchiveEntry Entry(string name)
        {
            string fullName = prefix + name;
            return zip.CreateEntry(unflaggedUtf8 ? Encoding.Latin1.GetString(encoding.GetBytes(fullName)) : fullName);
        }

        void Png(string name, SKColor color, int x = -1, int y = -1, int width = 4, int height = 4)
        {
            using var image = new SKBitmap(width, height);
            image.Erase(x < 0 ? color : SKColors.Transparent);
            if (x >= 0)
                image.SetPixel(x, y, color);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            using Stream entry = Entry(name).Open();
            png.SaveTo(entry);
        }

        // Deliberately shuffle the entries. Directory records are optional in a ZIP.
        Png("全/200.png", SKColors.Magenta, 0, 0);
        Png("眉/00.png", SKColors.Yellow, 2, 2);
        Png("目/07a-1.png", SKColors.Black, 1, 1);
        Png("目/07-1.png", SKColors.Blue, 1, 1);
        Png("目/00.png", SKColors.Green, 1, 1);
        Png("体/00.png", SKColors.Red);
        Png("顔/00a.png", SKColors.Transparent);
        Png("顔/02b.png", SKColors.Gray);
        Png("後/01u1.png", SKColors.Blue, width: 8, height: 6);
        foreach (string name in new[] { "00", "00a", "00b", "00c" })
            Png($"口/{name}.png", SKColors.Black, 1, 3);
        zip.CreateEntry("__MACOSX/" + prefix + "体/._00.png");
        zip.CreateEntry(prefix + "説明/見本.png");
        using Stream sets = Entry("全/セット.txt").Open();
        sets.Write(encoding.GetBytes("表情01=顔02-眉00-目07-口00-体00\r\n不明=口99"));
    }

    public void Dispose() => File.Delete(Path);
}
