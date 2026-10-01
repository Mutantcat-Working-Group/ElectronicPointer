namespace Mutantcat.ElectronicPointer.Core.Screens;

/// <summary>
/// Where one screen sits, in pixels, in whichever space the host toolkit reports it. The
/// capture backend and the windowing toolkit each name the desk their own way, so the app
/// keeps the rectangle of both and compares those: a list index is a guess about the order
/// two independent enumerations happen to agree on, and it is exactly the guess that a
/// desk with two identical monitors breaks.
/// </summary>
public readonly record struct ScreenPlacement(int X, int Y, int Width, int Height)
{
    public bool SameSize(ScreenPlacement other) => Width == other.Width && Height == other.Height;
}
