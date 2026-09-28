using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using SkiaSharp;
using Xunit;

namespace Mutantcat.ElectronicPointer.Rendering.Tests.Rendering;
public class BoardRendererTests
{
    private static BoardPage PageWithHorizontalStroke(StrokeStyle style, double y = 100)
    {
        var page = new BoardPage();
        var stroke = new Stroke(style);
        for (var x = 20; x <= 380; x += 10)
            stroke.Append(new Vec2(x, y), 0.5);
        stroke.Complete();
        page.Strokes.Add(stroke);
        return page;
    }

    [Fact]
    public void Render_PenStroke_PaintsInkAlongTheSampledPath()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 40));

        using var bitmap = BitmapExporter.Render(page, 400, 200);

        Assert.True(bitmap.GetPixel(200, 100).Alpha > 200, "expected ink where the pen travelled");
        Assert.True(bitmap.GetPixel(200, 115).Alpha > 200, "expected ink inside the stroke width");
        Assert.True(bitmap.GetPixel(200, 70).Alpha == 0, "expected no ink far from the stroke");
        Assert.True(bitmap.GetPixel(200, 130).Alpha == 0, "expected no ink below the stroke");
    }

    [Fact]
    public void Render_TransparentSurface_StaysClearOutsideTheInk()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 40));

        using var bitmap = BitmapExporter.Render(page, 400, 200);

        // The whole app draws over the live desktop, so an untouched surface must stay see-through.
        foreach (var x in new[] { 5, 50, 200, 396 })
        {
            Assert.True(bitmap.GetPixel(x, 5).Alpha == 0, $"row {x} should stay transparent");
            Assert.True(bitmap.GetPixel(x, 195).Alpha == 0, $"row {x} should stay transparent");
        }
    }

    [Fact]
    public void Render_Highlighter_IsTranslucent()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreateHighlighter(0x60FFE14D, 40));

        using var bitmap = BitmapExporter.Render(page, 400, 200);

        var pixel = bitmap.GetPixel(200, 100);
        Assert.True(pixel is { Red: 255, Green: 226, Blue: 77 }, $"unexpected highlighter colour: {pixel}");
        Assert.True(pixel.Alpha is > 20 and < 240, $"highlighter should stay see-through, alpha={pixel.Alpha}");
    }

    [Fact]
    public void Render_PageBackground_FillsTheWholeSurface()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 8));
        page.BackgroundColor = 0xFF102030;

        using var bitmap = BitmapExporter.Render(page, 120, 90, 0xFFFFFFFF);

        Assert.Equal(0xFF102030u, bitmap.GetPixel(60, 45).ToPackedArgb());
    }

    [Fact]
    public void Render_IgnoresStrokesOnOtherPages()
    {
        var document = new BoardDocument();
        var first = document.AddPage();
        first.Strokes.Add(new Stroke(StrokeStyle.CreatePen(0xFF000000, 40)));
        var second = document.AddPage();
        var stroke = new Stroke(StrokeStyle.CreatePen(0xFF000000, 40));
        for (var x = 20; x <= 380; x += 10)
            stroke.Append(new Vec2(x, 100), 0.5);
        second.Strokes.Add(stroke);

        using var renderer = new BoardRenderer();
        using var bitmap = new SKBitmap(400, 200);
        using (var canvas = new SKCanvas(bitmap))
        {
            renderer.Render(canvas, document, RenderOptions.Default with { Width = 400, Height = 200 });
        }

        Assert.True(bitmap.GetPixel(200, 100).Alpha == 0, "the inactive page must not show through");
    }

    [Fact]
    public void Render_LiveStroke_ShowsWhileThePenIsDown()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 8));
        var active = new Stroke(StrokeStyle.CreatePen(0xFF000000, 60));
        for (var x = 40; x <= 300; x += 10)
            active.Append(new Vec2(x, 60), 0.5);
        page.ActiveStroke = active;

        using var bitmap = BitmapExporter.Render(page, 400, 200);

        Assert.True(bitmap.GetPixel(160, 60).Alpha > 200, "the stroke under the pen must be visible");
    }

    [Fact]
    public void Render_AfterReset_PaintsTheSameInkAgain()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 40));
        using var renderer = new BoardRenderer();
        var options = RenderOptions.Default with { Width = 400, Height = 200 };

        using var first = new SKBitmap(400, 200);
        using var second = new SKBitmap(400, 200);
        using (var canvas = new SKCanvas(first))
            renderer.Render(canvas, page, options);

        renderer.Reset();

        using (var canvas = new SKCanvas(second))
            renderer.Render(canvas, page, options);

        for (var x = 0; x < 400; x += 17)
        {
            Assert.Equal(first.GetPixel(x, 100), second.GetPixel(x, 100));
        }
    }

    [Fact]
    public void Render_Scale_KeepsStrokeGeometryProportional()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 40));

        using var single = BitmapExporter.Render(page, 400, 200, null, 1f);
        using var double_ = BitmapExporter.Render(page, 800, 400, null, 2f);

        Assert.Equal(single.GetPixel(200, 100).Alpha, double_.GetPixel(400, 200).Alpha);
    }

    [Fact]
    public void EncodePng_RoundTripsThroughTheDecoder()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 40));

        using var bitmap = BitmapExporter.Render(page, 400, 200, 0xFFFFFFFF);
        var png = BitmapExporter.EncodePng(bitmap);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);

        using var decoded = SKBitmap.Decode(png);
        Assert.Equal(400, decoded.Width);
        Assert.Equal(200, decoded.Height);
        Assert.Equal(bitmap.GetPixel(200, 100), decoded.GetPixel(200, 100));
    }

    [Fact]
    public void EncodeJpeg_UsesTheJpegSignature()
    {
        var page = PageWithHorizontalStroke(StrokeStyle.CreatePen(0xFF000000, 40));

        using var bitmap = BitmapExporter.Render(page, 400, 200, 0xFFFFFFFF);
        var jpeg = BitmapExporter.EncodeJpeg(bitmap);

        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, jpeg[..3]);
    }
}
