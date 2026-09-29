namespace Mutantcat.ElectronicPointer.Core.Recognition;

/// <summary>
/// The shapes the built-in recognizer can tidy a hand drawn stroke into. <see cref="None"/>
/// is what a genuine scribble comes back as, and it means the ink is left exactly as drawn.
/// </summary>
public enum ShapeKind
{
    None,
    Line,
    Arrow,
    Rectangle,
    Triangle,
    Ellipse,
}
