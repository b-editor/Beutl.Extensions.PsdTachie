using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Beutl.Controls.Styling.Themes;
using Beutl.Extensions.PsdTachie.Tests;
using FluentAvalonia.Styling;

[assembly: AvaloniaTestApplication(typeof(HeadlessApp))]

namespace Beutl.Extensions.PsdTachie.Tests;

/// <summary>A headless application using Beutl's styles to exercise the property editors without a display.</summary>
public sealed class HeadlessApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseHarfBuzz()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        var baseUri = new Uri("avares://Beutl.Controls/");
        Resources.MergedDictionaries.Add(new ResourceInclude(baseUri)
        {
            Source = new Uri("avares://Beutl.Controls/Styling/Fonts.axaml"),
        });
        // Match Beutl's default dark theme, while leaving light captures on its light resources.
        Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            MergedDictionaries = { new ResourceInclude(baseUri) { Source = BeutlDarkBorderTheme.ResourceUri } },
        };
        Styles.Add(new FluentAvaloniaTheme
        {
            PreferUserAccentColor = true,
            TextVerticalAlignmentOverrideBehavior = TextVerticalAlignmentOverride.Disabled,
        });
        Styles.Add(new StyleInclude(baseUri)
        {
            Source = new Uri("avares://Beutl.Controls/Styles.axaml"),
        });
    }
}
