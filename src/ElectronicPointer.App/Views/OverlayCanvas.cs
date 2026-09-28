using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Mutantcat.ElectronicPointer.App.Session;
using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using Mutantcat.ElectronicPointer.Rendering;

// Avalonia.Media carries a RenderOptions of its own, so the ink one is named explicitly.
using RenderOptions = Mutantcat.ElectronicPointer.Rendering.RenderOptions;

namespace Mutantcat.ElectronicPointer.App.Views;

/// <summary>
/// The surface ink is collected on and drawn to. Painting goes through Skia, the one
/// rendering path every Avalonia desktop backend shares, so ink looks the same on Windows,
/// macOS and Linux and matches the offscreen export pixel for pixel.
///
/// A window only shows a slice of the board, so the control is told where its own top-left
/// corner sits on that board. Pointer positions become board coordinates by adding that
/// offset, and drawing subtracts it again, which keeps a stroke on the screen it was drawn
/// on instead of sliding onto the first one.
/// </summary>
public sealed class OverlayCanvas : Control
{
    private readonly BoardRenderer _renderer = new();
    private WriteableBitmap? _fallbackSurface;
    private SKBitmap? _fallbackTarget;
    private PixelSize _fallbackSize;

    public BoardSession? Session { get; private set; }

    /// <summary>Board coordinate of this surface's top-left corner.</summary>
    public Vec2 Origin { get; private set; } = Vec2.Zero;

    public OverlayCanvas()
    {
        ClipToBounds = true;
        Focusable = false;
    }

    public void Bind(BoardSession session, Vec2 origin)
    {
        if (Session is not null)
            Session.Changed -= OnSessionChanged;

        Session = session;
        Origin = origin;
        session.Changed += OnSessionChanged;
        RefreshChrome();
        InvalidateVisual();
    }

    public void Release()
    {
        if (Session is not null)
            Session.Changed -= OnSessionChanged;

        Session = null;
        _fallbackTarget?.Dispose();
        _fallbackTarget = null;
        _fallbackSurface = null;
    }

    /// <summary>Surface-local point in board coordinates.</summary>
    public Vec2 ToBoard(Point position) => new(position.X + Origin.X, position.Y + Origin.Y);

    public override void Render(DrawingContext context)
    {
        var session = Session;
        if (session is null)
            return;

        var scaling = (float)(VisualRoot?.RenderScaling ?? 1d);
        if (scaling <= 0 || float.IsNaN(scaling))
            scaling = 1;

        var deviceWidth = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        var deviceHeight = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));

        var options = RenderOptions.Default with
        {
            Width = deviceWidth,
            Height = deviceHeight,
            Scale = scaling,
            Origin = Origin,
        };

        if (session.Tool == ToolKind.Eraser && session.EraserCursorVisible)
        {
            var cursor = session.EraserCursor;
            options = options with
            {
                // The ring is drawn after the renderer restored its transform, so it is
                // expressed in device pixels rather than in board units.
                EraserCursor = new SKPoint(
                    (float)((cursor.X - Origin.X) * scaling),
                    (float)((cursor.Y - Origin.Y) * scaling)),
                EraserRadius = (float)(session.EraserRadius * scaling),
            };
        }

        var page = session.Document.ActivePage;
        DrawBitmap(context, page, session, options, deviceWidth, deviceHeight, scaling);
    }

    /// <summary>
    /// The lasso being pulled out, drawn here rather than in the renderer because it belongs
    /// to the gesture in this window and nowhere else.
    /// </summary>
    private void DrawGesture(SKCanvas canvas, BoardSession session, float scaling)
    {
        if (!session.IsLassoing)
            return;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * scaling,
            Color = new SKColor(0x0B, 0x6C, 0xD4),
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f * scaling, 4f * scaling }, 0),
        };

        using var path = new SKPath();
        foreach (var point in session.LassoLoop)
        {
            var x = (float)((point.X - Origin.X) * scaling);
            var y = (float)((point.Y - Origin.Y) * scaling);
            if (path.PointCount == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        canvas.DrawPath(path, paint);
    }

    /// <summary>
    /// Paints the board into an off-screen Skia bitmap and hands it to Avalonia as a
    /// <see cref="WriteableBitmap"/>. Avalonia 11.3 no longer exposes the live Skia canvas
    /// through <see cref="DrawingContext"/>, so this is not a fallback but the single
    /// rendering path: it is also the same code the PNG/JPEG export uses, which is what
    /// keeps the picture on screen and the picture on disk identical on every platform.
    /// </summary>
    private void DrawBitmap(
        DrawingContext context,
        BoardPage page,
        BoardSession session,
        RenderOptions options,
        int width,
        int height,
        float scaling)
    {
        var size = new PixelSize(width, height);
        if (_fallbackTarget is null || _fallbackSize != size)
        {
            _fallbackTarget?.Dispose();
            _fallbackTarget = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            _fallbackSurface = new WriteableBitmap(
                size,
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            _fallbackSize = size;
        }

        using (var canvas = new SKCanvas(_fallbackTarget!))
        {
            _renderer.Render(canvas, page, options);
            DrawGesture(canvas, session, scaling);
        }

        var pixels = new byte[_fallbackTarget!.RowBytes * height];
        Marshal.Copy(_fallbackTarget.GetPixels(), pixels, 0, pixels.Length);

        using var locked = _fallbackSurface!.Lock();
        Marshal.Copy(pixels, 0, locked.Address, pixels.Length);

        context.DrawImage(_fallbackSurface, new Rect(0, 0, width, height), new Rect(Bounds.Size));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var session = Session;
        if (session is null)
            return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(this);
            session.PointerPressed(ToBoard(point.Position), point.Properties.Pressure);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var session = Session;
        if (session is null)
            return;

        session.PointerMoved(ToBoard(e.GetPosition(this)), e.GetCurrentPoint(this).Properties.Pressure);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var session = Session;
        if (session is null)
            return;

        session.PointerReleased(ToBoard(e.GetPosition(this)));
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        Session?.Leave();
        base.OnPointerExited(e);
    }

    private void OnSessionChanged()
    {
        RefreshChrome();
        InvalidateVisual();
    }

    private void RefreshChrome()
    {
        var session = Session;
        if (session is null)
            return;

        Cursor = session.PassThrough
            ? new Cursor(StandardCursorType.Arrow)
            : session.Tool switch
            {
                ToolKind.Pen => new Cursor(StandardCursorType.Cross),
                ToolKind.Highlighter => new Cursor(StandardCursorType.Cross),
                ToolKind.Eraser => new Cursor(StandardCursorType.Cross),
                _ => new Cursor(StandardCursorType.Arrow),
            };
    }
}
