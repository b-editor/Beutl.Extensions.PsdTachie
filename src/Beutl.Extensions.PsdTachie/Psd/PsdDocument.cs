namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>A layered character image with its canvas and layer tree.</summary>
public sealed class PsdDocument
{
    internal PsdDocument(int width, int height, PsdLayer root)
    {
        Width = width;
        Height = height;
        Root = root;
        var all = new List<PsdLayer>();
        AssignPaths(root, all);
        AllLayers = all;
    }

    internal TachieZip? Zip { get; init; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The invisible root group. Its children are ordered bottom to top, as Photoshop stores them.</summary>
    public PsdLayer Root { get; }

    /// <summary>Every layer and group except the root, in depth-first bottom-to-top order.</summary>
    public IReadOnlyList<PsdLayer> AllLayers { get; }

    public PsdLayer? FindByPath(string path)
    {
        foreach (PsdLayer layer in AllLayers)
        {
            if (layer.Path == path)
                return layer;
        }

        return null;
    }

    private static void AssignPaths(PsdLayer root, List<PsdLayer> all)
    {
        Visit(root, "");

        void Visit(PsdLayer parent, string prefix)
        {
            // Number duplicates from the top, the order the layer panel shows them in.
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var paths = new string[parent.Children.Count];
            for (int i = parent.Children.Count - 1; i >= 0; i--)
            {
                string escaped = EscapeName(parent.Children[i].Name);
                int n = seen.TryGetValue(escaped, out int count) ? count + 1 : 1;
                seen[escaped] = n;
                paths[i] = prefix + (n == 1 ? escaped : $"{escaped}#{n}");
            }

            for (int i = 0; i < parent.Children.Count; i++)
            {
                PsdLayer child = parent.Children[i];
                child.Path = paths[i];
                child.Index = all.Count;
                all.Add(child);
                if (child.IsGroup)
                    Visit(child, child.Path + "/");
            }
        }
    }

    internal static string EscapeName(string name)
    {
        return name.Replace("\\", "\\\\").Replace("/", "\\/").Replace("#", "\\#");
    }
}
