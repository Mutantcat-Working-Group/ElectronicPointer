namespace Mutantcat.ElectronicPointer.Platform;

/// <summary>A piece of desktop behaviour that not every operating system allows.</summary>
public enum PlatformFeature
{
    /// <summary>Make the overlay window ignore mouse input while the pen is off.</summary>
    ClickThroughOverlay,

    /// <summary>Keep the overlay above every other window, including full screen apps.</summary>
    AlwaysOnTopOverlay,

    /// <summary>Hide the overlay from the dock, task bar and window switcher.</summary>
    HideFromTaskSwitcher,

    /// <summary>Fire while another application is focused.</summary>
    GlobalHotkey,

    /// <summary>Grab the pixels behind the overlay, used by screen freeze.</summary>
    ScreenCapture,

    /// <summary>Detect a running slideshow, used to ink on presentation slides.</summary>
    PresentationDetection,

    /// <summary>Tidy hand drawn strokes into proper shapes, offline and undoable.</summary>
    HandwritingRecognition,

    /// <summary>Launch the app when the user logs in.</summary>
    AutoStart,

    /// <summary>Position a window across a specific display.</summary>
    PerDisplayPlacement,
}

/// <summary>
/// What the host can actually do. The app reads this once at startup and disables the
/// corresponding UI instead of failing later, so a feature that one platform cannot
/// support never breaks the rest of the experience.
/// </summary>
public sealed record PlatformCapabilities
{
    private readonly HashSet<PlatformFeature> _features;

    public PlatformCapabilities(IEnumerable<PlatformFeature> features, string? notes = null)
    {
        _features = new HashSet<PlatformFeature>(features);
        Notes = notes;
    }

    public static PlatformCapabilities None { get; } = new(Array.Empty<PlatformFeature>());

    /// <summary>Short, user facing explanation of what is missing, in Chinese.</summary>
    public string? Notes { get; }

    public IReadOnlyCollection<PlatformFeature> Features => _features;

    public bool Supports(PlatformFeature feature) => _features.Contains(feature);

    public PlatformCapabilities With(PlatformFeature feature) => new(_features.Append(feature), Notes);

    public PlatformCapabilities WithAll(params PlatformFeature[] features) => new(_features.Concat(features), Notes);

    public PlatformCapabilities Without(PlatformFeature feature) => new(_features.Where(f => f != feature), Notes);
}
