using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Extensions.PsdTachie.Graphics;

/// <summary>Draws one composited frame of a PSD at its native resolution.</summary>
internal sealed class PsdBitmapRenderNode(Bitmap bitmap) : RenderNode
{
    private static readonly RenderResourceSlot<Bitmap> s_bitmapSlot = new();

    public Bitmap Bitmap { get; private set; } = bitmap;

    public bool Update(Bitmap bitmap)
    {
        if (ReferenceEquals(Bitmap, bitmap))
            return false;

        Bitmap = bitmap;
        MarkChanged();
        return true;
    }

    public override void Process(RenderNodeContext context)
    {
        Bitmap bitmap = Bitmap;
        if (bitmap.IsDisposed || bitmap.Width <= 0 || bitmap.Height <= 0)
            return;

        var bounds = new Rect(0, 0, bitmap.Width, bitmap.Height);
        RenderResource<Bitmap> token = context.Borrow(bitmap);
        context.Publish(context.PaintedSource(
            bitmap,
            static (canvas, fill, _, state) => canvas.DrawBitmap(state, fill, null),
            Brushes.Resource.White,
            null,
            bounds,
            RenderHitTestContract.OutputBounds,
            RenderScaleContract.Custom(static _ => 1f),
            bindings: [s_bitmapSlot.Bind(token)]));
    }
}
