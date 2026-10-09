namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>One layer or group of a <see cref="PsdDocument"/>.</summary>
public sealed class PsdLayer
{
    private readonly List<PsdLayer> _children = [];

    internal PsdLayer(string name, bool isGroup)
    {
        Name = name;
        IsGroup = isGroup;
    }

    public string Name { get; internal set; }

    public bool IsGroup { get; }

    public PsdLayer? Parent { get; internal set; }

    /// <summary>Children ordered bottom to top.</summary>
    public IReadOnlyList<PsdLayer> Children => _children;

    /// <summary>
    /// A path of escaped names joined by '/'. Siblings sharing a name get a "#2", "#3"... suffix so every
    /// path is unique within the document.
    /// </summary>
    public string Path { get; internal set; } = "";

    /// <summary>The index of this layer in <see cref="PsdDocument.AllLayers"/>.</summary>
    public int Index { get; internal set; }

    /// <summary>Whether the layer is visible in the saved document.</summary>
    public bool IsVisibleByDefault { get; internal set; }

    /// <summary>A name starting with '*' makes the layer one choice of a radio group among its '*' siblings.</summary>
    public bool IsRadio => Name.StartsWith('*');

    /// <summary>A name starting with '!' keeps the layer visible regardless of the requested state.</summary>
    public bool IsForced => Name.StartsWith('!');

    /// <summary>The layer name without the leading '*' or '!' marker.</summary>
    public string DisplayName => IsRadio || IsForced ? Name[1..] : Name;

    public byte Opacity { get; internal set; } = 255;

    public byte FillOpacity { get; internal set; } = 255;

    /// <summary>The four-character Photoshop blend mode key, such as "norm", "mul " or "pass".</summary>
    public string BlendModeKey { get; internal set; } = "norm";

    /// <summary>Whether the layer is clipped to the nearest non-clipped layer below it.</summary>
    public bool IsClipped { get; internal set; }

    public int Left { get; internal set; }

    public int Top { get; internal set; }

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    public PsdLayerMask? Mask { get; internal set; }

    internal List<PsdChannel> Channels { get; } = [];

    internal void AddChild(PsdLayer child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void InsertChild(int index, PsdLayer child)
    {
        child.Parent = this;
        _children.Insert(index, child);
    }

    public override string ToString() => Path;
}

/// <summary>A layer's user mask: an 8-bit coverage image placed in document coordinates.</summary>
public sealed class PsdLayerMask
{
    internal PsdLayerMask(int left, int top, int width, int height, byte defaultColor, byte[] pixels)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        DefaultColor = defaultColor;
        Pixels = pixels;
    }

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The coverage outside the mask rectangle: 0 hides, 255 shows.</summary>
    public byte DefaultColor { get; }

    /// <summary>Row-major coverage, <see cref="Width"/> × <see cref="Height"/> bytes.</summary>
    public byte[] Pixels { get; }
}

internal sealed class PsdChannel(short id, long length)
{
    public short Id { get; } = id;

    public long Length { get; } = length;

    /// <summary>Decoded samples, normalised to 8 bits per pixel. Null until the image data section is read.</summary>
    public byte[]? Data { get; set; }
}
