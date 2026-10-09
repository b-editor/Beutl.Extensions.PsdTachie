using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Beutl.Extensions.PsdTachie.Tests;

[assembly: AvaloniaTestApplication(typeof(HeadlessApp))]

namespace Beutl.Extensions.PsdTachie.Tests;

/// <summary>A headless Avalonia application for exercising the property editors without a display.</summary>
public sealed class HeadlessApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
