using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using SkiaSharp;
using Xunit;

namespace Mutantcat.ElectronicPointer.Rendering.Tests.Rendering;

/// <summary>
/// The freeze-screen feature grabs a display and draws ink over the still image. These tests
/// pin the BGRA, top-row-first contract the platform capture services promise, so a capture
/// that returns the wrong row order or the wrong channel order fails here instead of showing
/// up as a flipped or tinted screenshot at runtime.
/// </summary>
public class FrozenBackgroundTests
{
    private const int BackgroundSize = 32;

    private static BoardBackground SolidBackground(byte b, byte g, byte r)
    {
        var pixels = new byte[BackgroundSize * BackgroundSize * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 0xFF;
        }

        return new BoardBackground(pixels, BackgroundSize, BackgroundSize);
    }

    [Fact]
    public void Render_BackgroundImage_PaintsTheFrozenScreen()
    {
        var page = new BoardPage { BackgroundImage = SolidBackground(0xC8, 0x64, 0x32) };

        using var bitmap = BitmapExporter.Render(page, BackgroundSize, BackgroundSize);

        Assert.Equal(new SKColor(0x32, 0x64, 0xC8), bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0x32, 0x64, 0xC8), bitmap.GetPixel(BackgroundSize - 1, BackgroundSize - 1));
    }

    [Fact]
    public void Render_BackgroundImage_KeepsRowsTopDown()
    {
        var pixels = new byte[4 * 2 * 2];
        // First row: blue. Second row: yellow. A bottom-up buffer would swap them.
        WriteRow(pixels, 0, 2, 0xFF, 0x00, 0x00);
        WriteRow(pixels, 1, 2, 0x00, 0xFF, 0xFF);

        var page = new BoardPage { BackgroundImage = new BoardBackground(pixels, 2, 2) };

        using var bitmap = BitmapExporter.Render(page, 2, 2);

        Assert.Equal(new SKColor(0x00, 0x00, 0xFF), bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0xFF, 0xFF, 0x00), bitmap.GetPixel(0, 1));
    }

    [Fact]
    public void Render_BackgroundImage_DoesNotChangeTheSurfaceWhenThereIsNoInk()
    {
        var page = new BoardPage { BackgroundImage = SolidBackground(0x10, 0x20, 0x30) };

        using var bitmap = BitmapExporter.Render(page, BackgroundSize, BackgroundSize);

        foreach (var (x, y) in new[] { (0, 0), (5, 5), (BackgroundSize - 1, 20) })
        {
            Assert.Equal(new SKColor(0x30, 0x20, 0x10), bitmap.GetPixel(x, y));
        }
    }

    [Fact]
    public void Render_InkOverFrozenScreen_SitsOnTopOfThePicture()
    {
        var middle = BackgroundSize / 2;
        var page = new BoardPage { BackgroundImage = SolidBackground(0x10, 0x20, 0x30) };
        var stroke = new Stroke(StrokeStyle.CreatePen(0xFFFFFFFF, 6));
        for (var x = 0; x <= 3; x += 1)
            stroke.Append(new Vec2(x, middle), 0.5);
        stroke.Complete();
        page.Strokes.Add(stroke);

        using var painted = BitmapExporter.Render(page, BackgroundSize, BackgroundSize);

        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), painted.GetPixel(1, middle));
        Assert.Equal(new SKColor(0x30, 0x20, 0x10), painted.GetPixel(1, BackgroundSize - 1));
    }

    [Fact]
    public void Render_BackgroundImage_ScalesWithTheSurface()
    {
        var middle = BackgroundSize / 2;
        var page = new BoardPage { BackgroundImage = SolidBackground(0x10, 0x20, 0x30) };

        using var small = BitmapExporter.Render(page, BackgroundSize, BackgroundSize, null, 1f);
        using var large = BitmapExporter.Render(page, BackgroundSize * 2, BackgroundSize * 2, null, 2f);

        Assert.Equal(small.GetPixel(middle, middle), large.GetPixel(BackgroundSize, BackgroundSize));
    }

    private static void WriteRow(byte[] pixels, int row, int width, byte b, byte g, byte r)
    {
        for (var x = 0; x < width; x++)
        {
            var offset = (row * width + x) * 4;
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = 0xFF;
        }
    }
}
