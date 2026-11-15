namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>The drawing order, expression sets and animation frames of a part-based character.</summary>
internal sealed class TachieZip
{
    private readonly Dictionary<string, PsdLayer> _folders;
    private readonly PsdLayer[] _paintOrder;
    private readonly Dictionary<PsdLayer, (PsdLayer? Middle, PsdLayer End)> _animations = [];
    private readonly Dictionary<string, PsdLayer[]> _presets = [];

    public TachieZip(PsdLayer root, string? presets)
    {
        _folders = root.Children.ToDictionary(g => g.Name);
        _paintOrder = root.Children.SelectMany(g => g.Children).OrderBy(PaintOrder).ToArray();
        var byName = new Dictionary<string, PsdLayer>();
        foreach (PsdLayer group in root.Children)
        {
            // A set refers to 顔00 or 目07 even when the actual files are 00a.png or 07-15.png.
            foreach (PsdLayer layer in group.Children.Reverse())
            {
                string name = layer.DisplayName;
                byName.TryAdd(group.Name + name, layer);
                int digits = DigitCount(name);
                if (digits > 0)
                    byName.TryAdd(group.Name + name[..digits], layer);
            }

            if (group.Name is "口" or "目")
                IndexAnimationFrames(group, byName);
        }

        if (presets != null)
            ReadPresets(presets, byName);
    }

    public IReadOnlyDictionary<string, PsdLayer[]> Presets => _presets;

    private static int PaintOrder(PsdLayer layer)
    {
        string name = layer.DisplayName;
        return layer.Parent!.Name switch
        {
            "後" => name.AsSpan(DigitCount(name)).StartsWith("m", StringComparison.Ordinal) ? 10 : 0,
            "体" => 1,
            "顔" => layer.BlendModeKey == "mul " ? 8 : 2,
            "髪" => name == "01" ? 7 : 3,
            "眉" => 4,
            "目" => 5,
            "口" => 6,
            "他" => 9,
            _ => 11, // 全
        };
    }

    private static int DigitCount(string name)
    {
        int count = 0;
        while (count < name.Length && char.IsAsciiDigit(name[count]))
            count++;
        return count;
    }

    private void IndexAnimationFrames(PsdLayer group, Dictionary<string, PsdLayer> byName)
    {
        foreach (PsdLayer layer in group.Children)
        {
            string name = layer.DisplayName;
            int digits = DigitCount(name);
            if (digits == 0 || (digits < name.Length && name[digits] is not ('-' or '+')))
                continue;

            var frames = new List<PsdLayer>();
            for (char frame = 'a'; frame <= 'j'; frame++)
            {
                if (byName.TryGetValue(group.Name + name[..digits] + frame + name[digits..], out PsdLayer? part))
                    frames.Add(part);
            }
            if (frames.Count > 0)
                _animations[layer] = (frames.Count > 1 ? frames[(frames.Count - 1) / 2] : null, frames[^1]);
        }
    }

    private void ReadPresets(string text, Dictionary<string, PsdLayer> byName)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            int equals = line.IndexOf('=');
            if (equals < 1)
                continue;
            string name = line[..equals].Trim();
            string[] tokens = line[(equals + 1)..].Split('-', StringSplitOptions.TrimEntries);
            if (name.Length > 0 && tokens.All(byName.ContainsKey))
                _presets[name] = tokens.Select(token => byName[token]).ToArray();
        }
    }

    public string ApplyPreset(PsdDocument document, string? state, PsdLayer[] parts)
    {
        bool[] flags = PsdLayerState.Resolve(document, state);
        if (_folders.TryGetValue("全", out PsdLayer? whole))
        {
            foreach (PsdLayer pose in whole.Children)
                flags[pose.Index] = false;
        }
        foreach (PsdLayer part in parts)
        {
            flags[part.Parent!.Index] = true;
            PsdLayerState.SetVisible(flags, part, true);
        }
        return PsdLayerState.Serialize(document, flags);
    }

    public (PsdLayer? Start, PsdLayer? Middle, PsdLayer? End) GetAnimation(string folder, bool[] flags)
    {
        PsdLayer? selected = Selected(folder, flags);
        return selected != null && _animations.TryGetValue(selected, out var frames)
            ? (selected, frames.Middle, frames.End)
            : default;
    }

    private PsdLayer? Selected(string folder, bool[] flags) =>
        _folders.TryGetValue(folder, out PsdLayer? group) && flags[group.Index]
            ? group.Children.FirstOrDefault(l => flags[l.Index])
            : null;

    public IEnumerable<(PsdLayer Layer, int OffsetY)> VisibleLayers(bool[] flags)
    {
        if (Selected("全", flags) is { } whole)
        {
            yield return (whole, 0);
            yield break;
        }

        // Eye parts can include the brows (z/x) or mouth (y/x), or move the brows, e.g. 07a-15.
        string eye = Selected("目", flags)?.DisplayName ?? "";
        int offsetAt = eye.IndexOfAny(['-', '+']);
        int browOffset = 0;
        if (offsetAt >= 0)
        {
            int.TryParse(eye.AsSpan(offsetAt), System.Globalization.CultureInfo.InvariantCulture, out browOffset);
            eye = eye[..offsetAt];
        }

        foreach (PsdLayer layer in _paintOrder)
        {
            PsdLayer group = layer.Parent!;
            if (!flags[layer.Index] || !flags[group.Index])
                continue;
            if (group.Name == "眉" && (eye.EndsWith('z') || eye.EndsWith('x')))
                continue;
            if (group.Name == "口" && (eye.EndsWith('y') || eye.EndsWith('x')))
                continue;
            yield return (layer, group.Name == "眉" ? browOffset : 0);
        }
    }
}
