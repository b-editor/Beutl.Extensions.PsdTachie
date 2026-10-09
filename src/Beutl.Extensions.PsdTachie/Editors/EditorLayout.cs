using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Beutl.Extensions.PsdTachie.Editors;

internal static class EditorLayout
{
    /// <summary>
    /// Lays out a property header above its editor, which fits the narrow property panel. The spacing is that of
    /// Beutl's own editors that do the same (GradientStopsEditor): 4px outside like every property editor, then 4px
    /// around the header and the editor, so both line up with the headers and values of the rows around them.
    /// </summary>
    public static Control WithHeader(string header, string? description, Control content)
    {
        var headerText = new TextBlock
        {
            Text = header,
            Margin = new Thickness(4, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(description))
            ToolTip.SetTip(headerText, description);

        return new StackPanel
        {
            Margin = new Thickness(4, 0),
            Children = { headerText, new Border { Padding = new Thickness(4), Child = content } },
        };
    }

    public static TextBlock CreateErrorText()
    {
        return new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0),
        };
    }
}
