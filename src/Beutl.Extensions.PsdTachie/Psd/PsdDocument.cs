namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>A parsed Photoshop document (PSD or PSB) with its layer tree.</summary>
public sealed class PsdDocument
{
    internal PsdDocument(int width, int height, PsdLayer root, IReadOnlyList<PsdLayer> allLayers)
    {
        Width = width;
        Height = height;
        Root = root;
        AllLayers = allLayers;
    }

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
}
