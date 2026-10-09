using Beutl.Extensions.PsdTachie.Psd;

namespace Beutl.Extensions.PsdTachie.Tests;

public class PsdLayerStateTests
{
    private static PsdDocument Load() =>
        PsdReader.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "tachie_rle.psd"));

    [Test]
    public void EmptyStateUsesSavedVisibility()
    {
        PsdDocument document = Load();
        bool[] flags = PsdLayerState.Resolve(document, "");

        Assert.That(flags[document.FindByPath("目/*開き")!.Index], Is.True);
        Assert.That(flags[document.FindByPath("目/*閉じ")!.Index], Is.False);
        Assert.That(flags[document.FindByPath("非表示")!.Index], Is.False);
    }

    [Test]
    public void SerializeStoresOnlyDifferencesAndRoundTrips()
    {
        PsdDocument document = Load();
        bool[] flags = PsdLayerState.Resolve(document, "");
        PsdLayerState.SetVisible(flags, document.FindByPath("目/*閉じ")!, true);
        flags[document.FindByPath("非表示")!.Index] = true;

        string text = PsdLayerState.Serialize(document, flags);

        Assert.That(text.Split('\n'), Is.EquivalentTo(new[] { "-目/*開き", "+目/*閉じ", "+非表示" }));
        Assert.That(PsdLayerState.Resolve(document, text), Is.EqualTo(flags));
    }

    [Test]
    public void ShowingRadioLayerHidesRadioSiblings()
    {
        PsdDocument document = Load();
        bool[] flags = PsdLayerState.Resolve(document, "+目/*半目");

        // The stored state names two radio layers; the topmost one wins.
        Assert.That(flags[document.FindByPath("目/*半目")!.Index], Is.True);
        Assert.That(flags[document.FindByPath("目/*開き")!.Index], Is.False);
    }

    [Test]
    public void ChoicesOverrideStoredState()
    {
        PsdDocument document = Load();
        PsdLayer closed = document.FindByPath("口/*閉じ")!;
        PsdLayer open = document.FindByPath("口/*開き")!;

        bool[] flags = PsdLayerState.Resolve(document, "", new Dictionary<PsdLayer, bool> { [closed] = false, [open] = true });

        Assert.That(flags[open.Index], Is.True);
        Assert.That(flags[closed.Index], Is.False);
        Assert.That(flags[document.FindByPath("口/*半開き")!.Index], Is.False);
    }

    [Test]
    public void UnknownPathsAreIgnored()
    {
        PsdDocument document = Load();
        Assert.That(PsdLayerState.Resolve(document, "+存在しない\n-これも"),
            Is.EqualTo(PsdLayerState.Resolve(document, "")));
    }

    [Test]
    public void EscapeNameEscapesPathSeparators()
    {
        Assert.That(PsdReader.EscapeName("a/b#c"), Is.EqualTo("a\\/b\\#c"));
    }
}
