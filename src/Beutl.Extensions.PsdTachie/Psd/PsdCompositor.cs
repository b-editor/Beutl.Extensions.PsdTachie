using SkiaSharp;

namespace Beutl.Extensions.PsdTachie.Psd;

/// <summary>Composites the visible layers of a <see cref="PsdDocument"/> into one bitmap.</summary>
/// <remarks>Instances cache decoded layer bitmaps and are safe to use from one thread at a time.</remarks>
public sealed class PsdCompositor : IDisposable
{
    private readonly Dictionary<int, SKBitmap?> _layerBitmaps = [];
    private readonly Lock _lock = new();
    private bool _disposed;

    public PsdCompositor(PsdDocument document)
    {
        Document = document;
    }

    public PsdDocument Document { get; }

    /// <summary>Renders the document. The caller owns the returned bitmap.</summary>
    /// <param name="flags">Each layer's own visibility flag, indexed by <see cref="PsdLayer.Index"/>.</param>
    public SKBitmap Compose(bool[] flags)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var info = new SKImageInfo(Math.Max(1, Document.Width), Math.Max(1, Document.Height),
                SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
            var bitmap = new SKBitmap(info);
            bitmap.Erase(SKColors.Empty);
            using var canvas = new SKCanvas(bitmap);
            DrawChildren(canvas, Document.Root, flags);
            canvas.Flush();
            return bitmap;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (SKBitmap? bitmap in _layerBitmaps.Values)
                bitmap?.Dispose();
            _layerBitmaps.Clear();
        }
    }

    private void DrawChildren(SKCanvas canvas, PsdLayer parent, bool[] flags)
    {
        IReadOnlyList<PsdLayer> children = parent.Children;
        int i = 0;
        while (i < children.Count)
        {
            PsdLayer layer = children[i];
            int end = i + 1;
            while (end < children.Count && children[end].IsClipped)
                end++;

            if (flags[layer.Index])
            {
                bool hasVisibleClip = false;
                for (int c = i + 1; c < end; c++)
                    hasVisibleClip |= flags[children[c].Index];

                if (hasVisibleClip)
                    DrawClippingGroup(canvas, children, i, end, flags);
                else
                    DrawLayer(canvas, layer, flags, layer.Opacity, ToBlendMode(layer.BlendModeKey));
            }

            i = end;
        }
    }

    private void DrawClippingGroup(SKCanvas canvas, IReadOnlyList<PsdLayer> children, int baseIndex, int end, bool[] flags)
    {
        PsdLayer baseLayer = children[baseIndex];

        // The clipping group takes the opacity and blend mode of its base layer.
        using (var groupPaint = new SKPaint { Color = SKColors.Black.WithAlpha(baseLayer.Opacity), BlendMode = ToBlendMode(baseLayer.BlendModeKey) })
        {
            canvas.SaveLayer(groupPaint);
        }

        DrawLayer(canvas, baseLayer, flags, 255, SKBlendMode.SrcOver);
        for (int c = baseIndex + 1; c < end; c++)
        {
            PsdLayer clipped = children[c];
            if (!flags[clipped.Index])
                continue;

            using (var clipPaint = new SKPaint { BlendMode = ToBlendMode(clipped.BlendModeKey) })
            {
                canvas.SaveLayer(clipPaint);
            }

            DrawLayer(canvas, clipped, flags, clipped.Opacity, SKBlendMode.SrcOver);
            DrawLayer(canvas, baseLayer, flags, 255, SKBlendMode.DstIn);
            canvas.Restore();
        }

        canvas.Restore();
    }

    private void DrawLayer(SKCanvas canvas, PsdLayer layer, bool[] flags, byte opacity, SKBlendMode blendMode)
    {
        if (layer.IsGroup)
        {
            bool passThrough = layer.BlendModeKey == "pass" && opacity == 255 && blendMode == SKBlendMode.SrcOver;
            if (passThrough && layer.Mask == null)
            {
                DrawChildren(canvas, layer, flags);
                return;
            }

            using (var paint = new SKPaint { Color = SKColors.Black.WithAlpha(opacity), BlendMode = layer.BlendModeKey == "pass" ? blendMode : ToBlendModeOr(layer.BlendModeKey, blendMode) })
            {
                canvas.SaveLayer(paint);
            }

            ApplyMaskClip(canvas, layer.Mask);
            DrawChildren(canvas, layer, flags);
            DrawMask(canvas, layer.Mask);
            canvas.Restore();
            return;
        }

        SKBitmap? bitmap = GetLayerBitmap(layer);
        if (bitmap == null)
            return;

        byte alpha = (byte)(opacity * layer.FillOpacity / 255);
        using var layerPaint = new SKPaint { Color = SKColors.Black.WithAlpha(alpha), BlendMode = blendMode };
        if (layer.Mask == null)
        {
            DrawBitmapAt(canvas, bitmap, layer.Left, layer.Top, layerPaint);
            return;
        }

        canvas.SaveLayer(layerPaint);
        ApplyMaskClip(canvas, layer.Mask);
        DrawBitmapAt(canvas, bitmap, layer.Left, layer.Top, null);
        DrawMask(canvas, layer.Mask);
        canvas.Restore();
    }

    private static void ApplyMaskClip(SKCanvas canvas, PsdLayerMask? mask)
    {
        // Outside its rectangle a mask takes its default colour; black hides everything there.
        if (mask is { DefaultColor: 0 })
            canvas.ClipRect(SKRect.Create(mask.Left, mask.Top, mask.Width, mask.Height));
    }

    private static void DrawMask(SKCanvas canvas, PsdLayerMask? mask)
    {
        if (mask == null || mask.Width <= 0 || mask.Height <= 0)
            return;

        var info = new SKImageInfo(mask.Width, mask.Height, SKColorType.Alpha8, SKAlphaType.Premul);
        using var maskBitmap = new SKBitmap();
        unsafe
        {
            fixed (byte* pixels = mask.Pixels)
            {
                maskBitmap.InstallPixels(info, (nint)pixels, mask.Width);
                using var paint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                DrawBitmapAt(canvas, maskBitmap, mask.Left, mask.Top, paint);
            }
        }
    }

    private static void DrawBitmapAt(SKCanvas canvas, SKBitmap bitmap, int x, int y, SKPaint? paint)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, x, y, SKSamplingOptions.Default, paint);
    }

    private SKBitmap? GetLayerBitmap(PsdLayer layer)
    {
        if (_layerBitmaps.TryGetValue(layer.Index, out SKBitmap? cached))
            return cached;

        SKBitmap? bitmap = CreateLayerBitmap(layer);
        _layerBitmaps[layer.Index] = bitmap;
        return bitmap;
    }

    internal static SKBitmap? CreateLayerBitmap(PsdLayer layer)
    {
        if (layer.Width <= 0 || layer.Height <= 0)
            return null;

        byte[]? red = null, green = null, blue = null, alpha = null;
        foreach (PsdChannel channel in layer.Channels)
        {
            switch (channel.Id)
            {
                case 0: red = channel.Data; break;
                case 1: green = channel.Data; break;
                case 2: blue = channel.Data; break;
                case -1: alpha = channel.Data; break;
            }
        }

        if (red == null)
            return null;

        // Grayscale documents only carry channel 0.
        green ??= red;
        blue ??= red;

        int count = layer.Width * layer.Height;
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);
        unsafe
        {
            byte* dst = (byte*)bitmap.GetPixels();
            for (int i = 0; i < count; i++)
            {
                dst[i * 4] = red[i];
                dst[i * 4 + 1] = green[i];
                dst[i * 4 + 2] = blue[i];
                dst[i * 4 + 3] = alpha?[i] ?? 255;
            }
        }

        bitmap.NotifyPixelsChanged();
        return bitmap;
    }

    private static SKBlendMode ToBlendModeOr(string key, SKBlendMode fallback)
    {
        SKBlendMode mode = ToBlendMode(key);
        return mode == SKBlendMode.SrcOver ? fallback : mode;
    }

    internal static SKBlendMode ToBlendMode(string key)
    {
        return key switch
        {
            "mul " => SKBlendMode.Multiply,
            "scrn" => SKBlendMode.Screen,
            "over" => SKBlendMode.Overlay,
            "dark" => SKBlendMode.Darken,
            "lite" => SKBlendMode.Lighten,
            "div " => SKBlendMode.ColorDodge,
            "idiv" => SKBlendMode.ColorBurn,
            "hLit" => SKBlendMode.HardLight,
            "sLit" => SKBlendMode.SoftLight,
            "diff" => SKBlendMode.Difference,
            "smud" => SKBlendMode.Exclusion,
            "hue " => SKBlendMode.Hue,
            "sat " => SKBlendMode.Saturation,
            "colr" => SKBlendMode.Color,
            "lum " => SKBlendMode.Luminosity,
            "lddg" => SKBlendMode.Plus,
            _ => SKBlendMode.SrcOver,
        };
    }
}
