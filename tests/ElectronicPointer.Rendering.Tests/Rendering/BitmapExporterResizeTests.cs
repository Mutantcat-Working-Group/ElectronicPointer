using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using SkiaSharp;
using Xunit;

namespace Mutantcat.ElectronicPointer.Rendering.Tests.Rendering;

/// <summary>
/// A frozen screen is grabbed at the display's native resolution and has to be scaled down to
/// the size the board works in. These tests pin what that conversion is allowed to change:
/// the dimensions, and nothing else that a person would notice.
/// </summary>
public class BitmapExporterResizeTests
{
    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }

    [Fact]
    public void Resize_HalvingASolidScreen_KeepsTheColourAndChangesTheSize()
    {
        using var source = Solid(64, 48, new SKColor(0x2E, 0x6B, 0xD4));

        using var resized = BitmapExporter.Resize(source, 32, 24);

        Assert.Equal(32, resized.Width);
        Assert.Equal(24, resized.Height);
        Assert.Equal(source.ColorType, resized.ColorType);
        foreach (var (x, y) in new[] { (0, 0), (31, 23), (16, 11), (5, 18) })
        {
            Assert.Equal(new SKColor(0x2E, 0x6B, 0xD4), resized.GetPixel(x, y));
        }
    }

    [Fact]
    public void Resize_Upscaling_KeepsTheSplitOfASharpEdge()
    {
        // Top half white, bottom half black, at 2x2. Upscaling must keep the halves where
        // they were rather than spreading one over the other.
        var source = new SKBitmap(2, 2);
        source.SetPixel(0, 0, SKColors.White);
        source.SetPixel(1, 0, SKColors.White);
        source.SetPixel(0, 1, SKColors.Black);
        source.SetPixel(1, 1, SKColors.Black);

        using var resized = BitmapExporter.Resize(source, 8, 8);

        Assert.Equal(8, resized.Width);
        Assert.Equal(8, resized.Height);
        Assert.Equal(SKColors.White, resized.GetPixel(0, 0));
        Assert.Equal(SKColors.White, resized.GetPixel(7, 0));
        Assert.Equal(SKColors.Black, resized.GetPixel(0, 7));
        Assert.Equal(SKColors.Black, resized.GetPixel(7, 7));
    }

    [Fact]
    public void Resize_DoesNotTouchTheSource()
    {
        using var source = Solid(16, 16, new SKColor(0x11, 0x22, 0x33));

        using var resized = BitmapExporter.Resize(source, 4, 4);

        Assert.Equal(16, source.Width);
        Assert.Equal(new SKColor(0x11, 0x22, 0x33), source.GetPixel(8, 8));
    }

    [Fact]
    public void Render_ThenResize_LandsTheInkWhereTheFrozenScreenIs()
    {
        var page = new BoardPage();
        var stroke = new Stroke(StrokeStyle.CreatePen(0xFF102030, 8));
        for (var x = 10; x <= 30; x += 5)
            stroke.Append(new Vec2(x, 50), 0.5);
        stroke.Complete();
        page.Strokes.Add(stroke);

        // Grabbed at 2x on a HiDPI display: twice the board's size in every direction.
        using var captured = BitmapExporter.Render(page, 80, 80, 0xFF0000FF, 2f);
        using var scaled = BitmapExporter.Resize(captured, 40, 40);

        Assert.Equal(40, scaled.Width);
        Assert.True(scaled.GetPixel(20, 25).Alpha > 200, "ink should survive the downscale");
        // 0xFF0000FF is packed ARGB, so the backdrop is opaque blue.
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF), scaled.GetPixel(2, 2));
    }
}
