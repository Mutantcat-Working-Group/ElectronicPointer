namespace Mutantcat.ElectronicPointer.Platform.Displays;

/// <summary>A physical display, described without any UI toolkit types.</summary>
public sealed record DisplayInfo
{
    /// <param name="x">Left edge, in pixels, in the virtual desktop space the host reports.</param>
    /// <param name="y">Top edge, in pixels, in the same space.</param>
    public DisplayInfo(
        int index,
        string name,
        int x,
        int y,
        int width,
        int height,
        double scaleFactor,
        bool isPrimary)
    {
        Index = index;
        Name = name;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ScaleFactor = scaleFactor;
        IsPrimary = isPrimary;
    }

    public int Index { get; init; }

    public string Name { get; init; }

    /// <summary>
    /// Left edge, in pixels, in the virtual desktop space the host reports. Two displays of
    /// the same size are indistinguishable without it, and a desk with two identical monitors
    /// is the ordinary case rather than the exotic one.
    /// </summary>
    public int X { get; init; }

    /// <summary>Top edge, in pixels, in the same space.</summary>
    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Physical pixels per device independent pixel.</summary>
    public double ScaleFactor { get; init; }

    public bool IsPrimary { get; init; }
}
