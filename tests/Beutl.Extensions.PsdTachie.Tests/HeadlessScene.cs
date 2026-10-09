using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Extensions.PsdTachie.Tests;

/// <summary>Renders a scene through Beutl's own pipeline without starting the editor.</summary>
internal static class HeadlessScene
{
    private static readonly Lazy<bool> s_isAvailable = new(() =>
        RenderThread.Dispatcher.Invoke(() => GraphicsContextFactory.GetOrCreateShared() is not null));

    public static void EnsureAvailable()
    {
        if (!s_isAvailable.Value)
            Assert.Ignore("No graphics context is available on this host.");
    }

    public static Scene Create(params (EngineObject Object, TimeSpan Length)[] objects)
    {
        var scene = new Scene(1920, 1080, "HeadlessScene");
        int layer = 0;
        foreach ((EngineObject obj, TimeSpan length) in objects)
        {
            var element = new Element { Start = TimeSpan.Zero, Length = length, ZIndex = layer++ };
            element.AddObject(obj);
            scene.AddChild(element);
        }

        return scene;
    }

    /// <summary>Renders one frame and returns it as 8-bit sRGB BGRA.</summary>
    public static Bitmap Render(Scene scene, TimeSpan time)
    {
        EnsureAvailable();
        return RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            CompositionFrame frame = renderer.Compositor.EvaluateGraphics(time);
            renderer.Render(frame);
            using Bitmap snapshot = renderer.Snapshot();
            return snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        });
    }

    public static (byte R, byte G, byte B, byte A) GetPixel(Bitmap bitmap, int x, int y)
    {
        Span<byte> row = bitmap.GetRow(y);
        return (row[x * 4 + 2], row[x * 4 + 1], row[x * 4], row[x * 4 + 3]);
    }

    /// <summary>Saves the frame next to the test binaries for a human to look at.</summary>
    public static void SaveForInspection(Bitmap bitmap, string name)
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "rendered");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        bitmap.Save(path, EncodedImageFormat.Png);
        TestContext.AddTestAttachment(path);
    }

    /// <summary>
    /// Splits the element at <paramref name="at"/> with Beutl's own split, as the timeline's split command does, and
    /// returns the back half. The split stores the back half next to the scene file, so the scene gets one first.
    /// </summary>
    public static Element Split(Scene scene, Element element, TimeSpan at)
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        scene.Uri = new Uri(Path.Combine(directory, "split.scene"));
        using var history = new HistoryManager(scene, new OperationSequenceGenerator());
        return new ElementStructureService(history).Split(scene, [element], at).NewElements.Single();
    }

    /// <summary>Asserts that two frames are identical, pixel for pixel, and that they show something.</summary>
    public static void AssertSameFrame(Bitmap expected, Bitmap actual)
    {
        int drawn = 0, different = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            ReadOnlySpan<byte> a = expected.GetRow(y);
            ReadOnlySpan<byte> b = actual.GetRow(y);
            for (int x = 0; x < expected.Width; x++)
            {
                if (a[x * 4 + 3] != 0)
                    drawn++;
                if (!a.Slice(x * 4, 4).SequenceEqual(b.Slice(x * 4, 4)))
                    different++;
            }
        }

        Assert.That(drawn, Is.GreaterThan(0), "The expected frame is empty.");
        Assert.That(different, Is.Zero, "Pixels that differ");
    }

    public static void AssertColor((byte R, byte G, byte B, byte A) actual, (byte R, byte G, byte B) expected, int tolerance = 4)
    {
        Assert.That(Math.Abs(actual.R - expected.R) <= tolerance
                    && Math.Abs(actual.G - expected.G) <= tolerance
                    && Math.Abs(actual.B - expected.B) <= tolerance,
            Is.True, $"Expected {expected} but was {actual}");
    }
}
