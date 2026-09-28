using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using Mutantcat.ElectronicPointer.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace Mutantcat.ElectronicPointer.Rendering.Tests.Rendering;

/// <summary>
/// A board saved to disk and reopened has to look exactly like the board that was saved. The
/// app exports its picture through the same renderer it draws with, so a serialization
/// round trip that shifts a colour, a width or a sample would show up both on screen and in
/// the PNG. Comparing the two bitmaps pixel by pixel catches that before a user does.
/// </summary>
public class SerializationRenderTests
{
    private const int Width = 240;
    private const int Height = 160;

    private static BoardDocument DocumentWithMixedInk()
    {
        var document = BoardDocument.CreateDefault();

        var pen = new Stroke(StrokeStyle.CreatePen(0xFF1B4F9C, 6));
        for (var x = 20; x <= 220; x += 12)
            pen.Append(new Vec2(x, 60 + (x % 24 == 0 ? 6 : 0)), 0.5);
        pen.Complete();
        document.ActivePage.Strokes.Add(pen);

        var highlighter = new Stroke(StrokeStyle.CreateHighlighter(0x60FFD84D, 26));
        for (var x = 30; x <= 210; x += 8)
            highlighter.Append(new Vec2(x, 120), 0.5);
        highlighter.Complete();
        document.ActivePage.Strokes.Add(highlighter);

        var second = document.AddPage("第二页");
        var marker = new Stroke(StrokeStyle.CreatePen(0xFFD43D2A, 10));
        for (var y = 30; y <= 130; y += 10)
            marker.Append(new Vec2(140, y), 0.5);
        marker.Complete();
        second.Strokes.Add(marker);
        document.ActivePage.BackgroundColor = 0xFFF2F2F2;

        return document;
    }

    [Fact]
    public void RoundTrip_KeepsEveryPageRenderedPixelForPixel()
    {
        var document = DocumentWithMixedInk();

        var before = RenderAllPages(document);
        var json = BoardDocumentSerializer.Serialize(document, AppIdentity.Version, AppIdentity.ApplicationId);
        var restored = BoardDocumentSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(document.Pages.Count, restored!.Pages.Count);

        var after = RenderAllPages(restored);

        Assert.Equal(before.Length, after.Length);
        for (var page = 0; page < before.Length; page++)
        {
            Assert.True(Identical(before[page], after[page]), $"page {page} rendered differently after a save and reopen");
        }
    }

    [Fact]
    public void Render_Twice_IsStableSoTheComparisonAboveMeansSomething()
    {
        var document = DocumentWithMixedInk();

        var first = RenderAllPages(document);
        var second = RenderAllPages(document);

        for (var page = 0; page < first.Length; page++)
        {
            Assert.True(Identical(first[page], second[page]), "the renderer should be deterministic");
        }
    }

    [Fact]
    public void RoundTrip_RestoresPageNamesAndTheActivePage()
    {
        var document = DocumentWithMixedInk();

        var json = BoardDocumentSerializer.Serialize(document, AppIdentity.Version, AppIdentity.ApplicationId);
        var restored = BoardDocumentSerializer.Deserialize(json)!;

        Assert.Equal(document.ActiveIndex, restored.ActiveIndex);
        Assert.Equal("第二页", restored.Pages[1].Name);
        Assert.Equal(0xFFF2F2F2u, restored.Pages[0].BackgroundColor);
    }

    private static SKBitmap[] RenderAllPages(BoardDocument document)
    {
        var result = new SKBitmap[document.Pages.Count];
        for (var i = 0; i < document.Pages.Count; i++)
            result[i] = BitmapExporter.Render(document.Pages[i], Width, Height);
        return result;
    }

    private static bool Identical(SKBitmap left, SKBitmap right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
            return false;

        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y) != right.GetPixel(x, y))
                    return false;
            }
        }

        return true;
    }
}
