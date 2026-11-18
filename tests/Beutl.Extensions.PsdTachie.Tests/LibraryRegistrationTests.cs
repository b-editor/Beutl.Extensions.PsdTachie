using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Extensions.PsdTachie.Graphics;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Extensions.PsdTachie.Tests;

public class LibraryRegistrationTests
{
    [Test]
    public void LibraryItemCanBeDroppedOnTheTimeline()
    {
        using var library = new LibraryService();
        using ServiceProvider services = new ServiceCollection().AddSingleton(library).BuildServiceProvider();
        new PsdTachieExtension().Load(new ExtensionContext(services));

        // The timeline only accepts library items that carry the EngineObject format; the Drawable format alone
        // shows in the library but is refused on drop.
        Assert.That(library.GetTypesFromFormat(KnownLibraryItemFormats.EngineObject), Does.Contain(typeof(PsdTachieDrawable)));
        Assert.That(library.GetTypesFromFormat(KnownLibraryItemFormats.Drawable), Does.Contain(typeof(PsdTachieDrawable)));
    }
}
