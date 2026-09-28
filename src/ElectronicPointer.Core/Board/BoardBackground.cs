namespace Mutantcat.ElectronicPointer.Core.Board;

/// <summary>
/// The picture a frozen screen became. Pixels are plain BGRA, first row first, with no
/// padding between rows, which is exactly what every platform capture service hands out,
/// so a grab can be dropped in without a conversion step and the board itself keeps no
/// dependency on any imaging library.
/// </summary>
public sealed class BoardBackground
{
    public BoardBackground(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A background image needs a size.");

        if (pixels.Length < width * height * 4)
            throw new ArgumentException("The pixel buffer is smaller than the image it describes.", nameof(pixels));

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <param name="originX">Board coordinate of the picture's left edge.</param>
    /// <param name="originY">Board coordinate of the picture's top edge.</param>
    public BoardBackground(byte[] pixels, int width, int height, double originX, double originY)
        : this(pixels, width, height)
    {
        OriginX = originX;
        OriginY = originY;
    }

    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Where the picture sits on the board. A screen is rarely the whole desktop, so a grab
    /// from the monitor on the right has to land on the right; without this it would always
    /// be drawn from the top-left corner of the first screen.
    /// </summary>
    public double OriginX { get; }

    public double OriginY { get; }

    /// <summary>Bytes per pixel row. Always <see cref="Width"/> * 4 with this layout.</summary>
    public int Stride => Width * 4;
}
