namespace Mutantcat.ElectronicPointer.Platform.Displays;

/// <summary>A physical display, described without any UI toolkit types.</summary>
public sealed record DisplayInfo
{
    public DisplayInfo(int index, string name, int width, int height, double scaleFactor, bool isPrimary)
    {
        Index = index;
        Name = name;
        Width = width;
        Height = height;
        ScaleFactor = scaleFactor;
        IsPrimary = isPrimary;
    }

    public int Index { get; }

    public string Name { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Physical pixels per device independent pixel.</summary>
    public double ScaleFactor { get; }

    public bool IsPrimary { get; }
}
