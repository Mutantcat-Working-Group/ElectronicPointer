using Mutantcat.ElectronicPointer.Core.Board;
using SkiaSharp;

namespace Mutantcat.ElectronicPointer.Rendering;

/// <summary>
/// Offscreen rendering used for "save the page as an image" and for the freeze-screen
/// feature. It runs on Skia's CPU raster backend, so it behaves the same on Windows,
/// macOS and Linux (and inside tests) without a GPU.
/// </summary>
public static class BitmapExporter
{
    /// <param name="background">Packed ARGB backdrop; <c>null</c> keeps the surface clear.</param>
    public static SKBitmap Render(BoardPage page, int width, int height, uint? background = null, float scale = 1f)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var renderer = new BoardRenderer();
        var options = RenderOptions.Default with
        {
            Width = width,
            Height = height,
            Scale = scale,
            Background = background is null ? SkiaColorExtensions.Transparent : SkiaColorExtensions.ToSkia(background.Value),
        };

        renderer.Render(canvas, page, options);
        return bitmap;
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 90)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    public static void WritePng(string path, BoardPage page, int width, int height, uint? background = null)
    {
        using var bitmap = Render(page, width, height, background);
        using var stream = File.Create(path);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Resamples a bitmap, used when a capture arrives in device pixels and has to be stored
    /// in board units instead. A frozen screen from a HiDPI display is twice the size the
    /// board works in, and keeping it that size would place the picture twice as large as
    /// the screen it came from.
    /// </summary>
    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        var resized = new SKBitmap(info);
        source.ScalePixels(resized, SKFilterQuality.Medium);
        return resized;
    }
}
