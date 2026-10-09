using System.Text;

namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>
/// Converts between per-layer visibility flags and the text stored on the drawable: one line per layer
/// whose visibility differs from the saved document, "+path" to show it and "-path" to hide it.
/// </summary>
/// <remarks>
/// The naming conventions of PSD character art are honoured: a '*' layer is a radio choice among its '*'
/// siblings, and a '!' layer is always shown.
/// </remarks>
public static class PsdLayerState
{
    public static Dictionary<string, bool> Parse(string? text)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
            return result;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length < 2)
                continue;

            if (line[0] == '+')
                result[line[1..]] = true;
            else if (line[0] == '-')
                result[line[1..]] = false;
        }

        return result;
    }

    public static string Serialize(PsdDocument document, IReadOnlyList<bool> flags)
    {
        var sb = new StringBuilder();
        foreach (PsdLayer layer in document.AllLayers)
        {
            bool visible = flags[layer.Index];
            if (visible != layer.IsVisibleByDefault && !layer.IsForced)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(visible ? '+' : '-').Append(layer.Path);
            }
        }

        return sb.ToString();
    }

    /// <summary>Resolves each layer's own visibility flag (ancestors are not taken into account).</summary>
    /// <param name="document">The document.</param>
    /// <param name="stateText">The stored state text.</param>
    /// <param name="choices">
    /// Layers to show or hide on top of the stored state, such as the current mouth or eye shape. A shown radio
    /// layer hides its radio siblings.
    /// </param>
    public static bool[] Resolve(PsdDocument document, string? stateText, IReadOnlyDictionary<PsdLayer, bool>? choices = null)
    {
        var flags = new bool[document.AllLayers.Count];
        foreach (PsdLayer layer in document.AllLayers)
            flags[layer.Index] = layer.IsVisibleByDefault;

        foreach ((string path, bool visible) in Parse(stateText))
        {
            if (document.FindByPath(path) is { } layer)
                flags[layer.Index] = visible;
        }

        NormalizeRadios(document.Root, flags);

        if (choices != null)
        {
            foreach ((PsdLayer layer, bool visible) in choices)
            {
                if (!visible)
                    flags[layer.Index] = false;
            }

            foreach ((PsdLayer layer, bool visible) in choices)
            {
                if (visible)
                    SetVisible(flags, layer, true);
            }
        }

        foreach (PsdLayer layer in document.AllLayers)
        {
            if (layer.IsForced)
                flags[layer.Index] = true;
        }

        return flags;
    }

    /// <summary>Sets a layer's flag, hiding its radio siblings when a radio layer is shown.</summary>
    public static void SetVisible(bool[] flags, PsdLayer layer, bool visible)
    {
        flags[layer.Index] = visible;
        if (visible && layer.IsRadio && layer.Parent != null)
        {
            foreach (PsdLayer sibling in layer.Parent.Children)
            {
                if (sibling != layer && sibling.IsRadio)
                    flags[sibling.Index] = false;
            }
        }
    }

    public static bool IsEffectivelyVisible(bool[] flags, PsdLayer layer)
    {
        for (PsdLayer? current = layer; current is { Parent: not null }; current = current.Parent)
        {
            if (!flags[current.Index])
                return false;
        }

        return true;
    }

    public static string CreateCacheKey(bool[] flags)
    {
        return string.Create(flags.Length, flags, static (span, f) =>
        {
            for (int i = 0; i < f.Length; i++)
                span[i] = f[i] ? '1' : '0';
        });
    }

    private static void NormalizeRadios(PsdLayer parent, bool[] flags)
    {
        // Only one radio child of a group may be shown; keep the topmost.
        bool found = false;
        for (int i = parent.Children.Count - 1; i >= 0; i--)
        {
            PsdLayer child = parent.Children[i];
            if (child.IsRadio && flags[child.Index])
            {
                if (found)
                    flags[child.Index] = false;
                found = true;
            }
        }

        foreach (PsdLayer child in parent.Children)
        {
            if (child.IsGroup)
                NormalizeRadios(child, flags);
        }
    }
}
