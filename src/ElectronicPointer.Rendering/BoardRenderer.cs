using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Ink;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Mutantcat.ElectronicPointer.Rendering;

/// <summary>A Skia path kept for a stroke until the stroke itself changes.</summary>
internal sealed class CachedStroke
{
    public CachedStroke(int revision, SKPath path)
    {
        Revision = revision;
        Path = path;
    }

    public int Revision { get; }

    public SKPath Path { get; }
}

/// <summary>
/// Draws a board document with Skia. Paths are cached per stroke until that stroke's own
/// revision moves, so a page with a lot of ink still repaints cheaply while a new stroke
/// is being drawn. This is the shared draw code behind both the interactive canvas and
/// the offscreen export, which keeps on-screen ink and exported images identical on
/// every platform.
/// </summary>
public sealed class BoardRenderer : IDisposable
{
    private readonly Dictionary<Stroke, CachedStroke> _paths = new(ReferenceEqualityComparer.Instance);
    private BoardBackground? _backgroundKey;
    private SKBitmap? _backgroundBitmap;
    private bool _disposed;

    public void Render(SKCanvas canvas, BoardDocument document, RenderOptions options)
    {
        Render(canvas, document.ActivePage, options);
    }

    public void Render(SKCanvas canvas, BoardPage page, RenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        canvas.Clear(options.Background);

        var scale = options.Scale <= 0 ? 1f : options.Scale;
        canvas.Save();

        // Board coordinates are absolute on the desktop; this surface only shows the part
        // that starts at the origin its window was told about.
        canvas.Translate(-(float)options.Origin.X, -(float)options.Origin.Y);
        canvas.Scale(scale);

        if (page.BackgroundColor != 0)
        {
            using var backdrop = new SKPaint { Color = SkiaColorExtensions.ToSkia(page.BackgroundColor), IsAntialias = true };
            canvas.DrawRect(new SKRect(0, 0, options.Width / scale, options.Height / scale), backdrop);
        }

        DrawBackgroundImage(canvas, page, options, scale);

        var live = new HashSet<Stroke>(ReferenceEqualityComparer.Instance);

        foreach (var stroke in page.Strokes)
        {
            DrawStroke(canvas, stroke);
            live.Add(stroke);
        }

        if (page.ActiveStroke is { Samples.Count: > 0 } active)
        {
            DrawStroke(canvas, active);
            live.Add(active);
        }

        canvas.Restore();

        Prune(live);
        DrawOverlays(canvas, options, scale);
    }

    /// <summary>Drops cached geometry, for instance after a document switch.</summary>
    public void Reset()
    {
        _backgroundBitmap?.Dispose();
        _backgroundBitmap = null;
        _backgroundKey = null;

        foreach (var cached in _paths.Values)
            cached.Path.Dispose();

        _paths.Clear();
    }

    /// <summary>
    /// Paints the frozen screen a page was snapshotted from, stretched across the whole
    /// surface so the picture fills whatever the overlay covers. The uploaded bitmap is kept
    /// until the page points somewhere else, so repainting costs no more than a plain
    /// transparent overlay.
    /// </summary>
    private void DrawBackgroundImage(SKCanvas canvas, BoardPage page, RenderOptions options, float scale)
    {
        var image = page.BackgroundImage;
        if (image is null)
            return;

        if (!ReferenceEquals(image, _backgroundKey) || _backgroundBitmap is null)
        {
            _backgroundBitmap?.Dispose();
            _backgroundBitmap = ToBitmap(image);
            _backgroundKey = image;
        }

        canvas.DrawBitmap(
            _backgroundBitmap!,
            new SKRect(0, 0, _backgroundBitmap!.Width, _backgroundBitmap.Height),
            new SKRect(
                (float)image.OriginX,
                (float)image.OriginY,
                (float)(image.OriginX + image.Width),
                (float)(image.OriginY + image.Height)));
    }

    private static SKBitmap ToBitmap(BoardBackground image)
    {
        var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var length = Math.Min(image.Width * image.Height * 4, image.Pixels.Length);
        Marshal.Copy(image.Pixels, 0, bitmap.GetPixels(), length);
        return bitmap;
    }

    private void DrawStroke(SKCanvas canvas, Stroke stroke)
    {
        if (!_paths.TryGetValue(stroke, out var cached) || cached.Revision != stroke.Revision)
        {
            cached?.Path.Dispose();
            cached = new CachedStroke(stroke.Revision, StrokeGeometry.BuildPath(stroke));
            _paths[stroke] = cached;
        }

        using var paint = StrokeGeometry.CreatePaint(stroke.Style);
        canvas.DrawPath(cached.Path, paint);
    }

    private void DrawOverlays(SKCanvas canvas, RenderOptions options, float scale)
    {
        if (options.Marquee is { } marquee)
        {
            var rect = new SKRect(marquee.Left, marquee.Top, marquee.Right, marquee.Bottom);
            using var fill = new SKPaint { Color = options.Accent.WithAlpha(48), IsAntialias = true };
            using var outline = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                Color = options.Accent,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new[] { 4f * scale, 3f * scale }, 0),
            };

            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, outline);
        }

        if (options.EraserCursor is { } cursor && options.EraserRadius > 0)
        {
            using var ring = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                Color = new SKColor(0x1B, 0x1B, 0x1F),
                IsAntialias = true,
            };

            canvas.DrawCircle(cursor.X, cursor.Y, options.EraserRadius, ring);
        }
    }

    private void Prune(HashSet<Stroke> live)
    {
        if (_paths.Count <= live.Count)
            return;

        List<Stroke>? dead = null;
        foreach (var stroke in _paths.Keys)
        {
            if (live.Contains(stroke))
                continue;

            dead ??= new List<Stroke>();
            dead.Add(stroke);
        }

        if (dead is null)
            return;

        foreach (var stroke in dead)
        {
            _paths[stroke].Path.Dispose();
            _paths.Remove(stroke);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Reset();
        GC.SuppressFinalize(this);
    }
}
