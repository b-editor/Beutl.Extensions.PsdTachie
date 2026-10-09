using Beutl.Services;
using Beutl.Extensions.PsdTachie.Graphics;

namespace Beutl.Extensions.PsdTachie.Tests;

public class LibraryRegistrationTests
{
    [Test]
    public void LibraryItemCanBeDroppedOnTheTimeline()
    {
        new PsdTachieExtension().Load();

        // The timeline only accepts library items that carry the EngineObject format; the Drawable format alone
        // shows in the library but is refused on drop.
        Assert.That(LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.EngineObject), Does.Contain(typeof(PsdTachieDrawable)));
        Assert.That(LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.Drawable), Does.Contain(typeof(PsdTachieDrawable)));
    }
}
