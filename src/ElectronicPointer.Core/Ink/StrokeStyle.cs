namespace Mutantcat.ElectronicPointer.Core.Ink;

/// <summary>How a stroke is painted. Only pen and highlighter produce ink today.</summary>
public enum StrokeKind
{
    Pen,
    Highlighter,
}

/// <summary>
/// Ink appearance of a stroke. Thumb-through: a pen is pressure sensitive
/// (<see cref="Thinning"/> above zero), a highlighter keeps a constant width.
/// </summary>
public sealed record StrokeStyle(
    StrokeKind Kind,
    uint Color,
    double Size,
    double Thinning = 0.5,
    bool SimulatePressure = true,
    double Smoothing = 0.5,
    double Streamline = 0.5,
    bool TaperEnds = false)
{
    private const uint BlackPen = 0xFF1B1B1F;

    private const uint YellowHighlighter = 0x60FFE14D;

    public static StrokeStyle CreatePen(uint color, double size) => new(StrokeKind.Pen, color, size, 0.5);

    public static StrokeStyle CreateHighlighter(uint color, double size)
        => new(StrokeKind.Highlighter, color, size, 0, false, 0.5, 0.5);

    public static StrokeStyle DefaultPen() => CreatePen(BlackPen, 3);

    public static StrokeStyle DefaultHighlighter() => CreateHighlighter(YellowHighlighter, 16);

    public static StrokeStyle Create(StrokeKind kind, uint color, double size) => kind == StrokeKind.Highlighter
        ? CreateHighlighter(color, size)
        : CreatePen(color, size);

    public StrokeOptions ToFreehandOptions(bool isComplete) => new(
        Size: Size,
        Thinning: Thinning,
        Smoothing: Smoothing,
        Streamline: Streamline,
        SimulatePressure: SimulatePressure,
        Start: TaperEnds ? new StrokeCapOptions(TaperEnabled: true) : null,
        End: TaperEnds ? new StrokeCapOptions(TaperEnabled: true) : null,
        Last: isComplete);
}

/// <summary>The tool the user is currently holding.</summary>
public enum ToolKind
{
    Pen,
    Highlighter,
    Eraser,
    Select,
    Pan,
}

/// <summary>Ink colour palette shown in the toolbar.</summary>
public static class InkPalette
{
    public static readonly uint[] Colors =
    {
        0xFF1B1B1F,
        0xFFD7263D,
        0xFF1B7F4B,
        0xFF0B6CD4,
        0xFFF5A524,
    };
}
