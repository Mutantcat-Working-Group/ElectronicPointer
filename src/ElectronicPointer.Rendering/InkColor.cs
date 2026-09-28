using SkiaSharp;

namespace Mutantcat.ElectronicPointer.Rendering;

/// <summary>
/// The board stores ink colours as packed ARGB uint so it can be serialised without any
/// dependency on a UI toolkit. These helpers convert between that representation and
/// Skia's, and are the single place the byte order is defined.
/// </summary>
public static class SkiaColorExtensions
{
    public static SKColor Transparent { get; } = new(0, 0, 0, 0);

    public static SKColor ToSkia(uint argb)
    {
        return new SKColor(
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));
    }

    public static uint ToPackedArgb(this SKColor color)
    {
        return ((uint)color.Alpha << 24) | ((uint)color.Red << 16) | ((uint)color.Green << 8) | color.Blue;
    }

    /// <summary>Premultiplied blend, used to check how much of a pixel a stroke covers.</summary>
    public static byte BlendAlpha(byte backdrop, byte ink)
    {
        return (byte)Math.Min(255, backdrop + ink);
    }
}
