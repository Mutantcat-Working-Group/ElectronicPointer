namespace Mutantcat.ElectronicPointer.Platform.Presentation;

/// <summary>
/// Tells the overlay that a slideshow just started or stopped so the pen can sit on top of
/// the slides without the toolbar getting in the way.
///
/// Windows reaches this through the PowerPoint add-in; other platforms watch for a
/// full screen presentation process or a user toggled mode, which is inherently weaker.
/// </summary>
public interface IPresentationBridge
{
    bool IsSupported { get; }

    bool IsPresentationActive { get; }

    event EventHandler<bool>? PresentationActiveChanged;

    void Start();

    void Stop();
}
