using Mutantcat.ElectronicPointer.Platform.Displays;

namespace Mutantcat.ElectronicPointer.Platform.Capture;

/// <summary>One grabbed screen: raw BGRA pixels, top row first, no padding between rows.</summary>
public sealed record CapturedScreen(byte[] Pixels, int Width, int Height, double ScaleFactor, string DisplayName)
{
    public int Stride => Width * 4;
}

public interface IScreenCaptureService
{
    bool IsSupported { get; }

    /// <summary>Displays the capture backend can actually reach.</summary>
    IReadOnlyList<DisplayInfo> Displays { get; }

    /// <summary>
    /// Grabs the pixels behind the overlay. Returns null when the platform refused, so the
    /// caller can fall back to a plain whiteboard page instead of failing.
    /// </summary>
    CapturedScreen? Capture(DisplayInfo display);
}
