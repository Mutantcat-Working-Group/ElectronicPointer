using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using SkiaSharp;

namespace Mutantcat.ElectronicPointer.Rendering;

/// <summary>
/// Turns a core <see cref="Stroke"/> into a Skia path. The outline already comes from
/// <see cref="Stroke.BuildOutline"/>, so this layer only fills the polygon; all ink
/// shaping decisions stay in the platform free core.
/// </summary>
public static class StrokeGeometry
{
    public static SKPath BuildPath(IReadOnlyList<Vec2> outline)
    {
        var path = new SKPath
        {
            FillType = SKPathFillType.Winding,
        };

        if (outline.Count < 3)
            return path;

        var points = new SKPoint[outline.Count];
        for (var i = 0; i < outline.Count; i++)
            points[i] = new SKPoint((float)outline[i].X, (float)outline[i].Y);

        path.AddPoly(points, true);
        return path;
    }

    public static SKPath BuildPath(Stroke stroke)
    {
        return BuildPath(stroke.BuildOutline());
    }

    /// <summary>
    /// Paint for a stroke. Highlighters keep their premultiplied alpha, and get a little
    /// blur so overlapping sweeps read as marker ink rather than solid colour.
    /// </summary>
    public static SKPaint CreatePaint(StrokeStyle style, float scale = 1f)
    {
        var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            Color = SkiaColorExtensions.ToSkia(style.Color),
        };

        if (style.Kind == StrokeKind.Highlighter)
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, scale * 0.5f);

        return paint;
    }
}
