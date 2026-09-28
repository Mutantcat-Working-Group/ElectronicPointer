using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Platform.Recognition;

/// <summary>
/// Turns ink into text. The old build shipped Windows Ink analysis for this; the portable
/// replacement is a swappable backend, because no OS outside Windows exposes a
/// handwriting engine the same way.
/// </summary>
public interface IHandwritingRecognizer
{
    bool IsSupported { get; }

    /// <summary>Empty when nothing could be read; never throws for bad input.</summary>
    Task<string> RecognizeAsync(IReadOnlyList<IReadOnlyList<Vec2>> strokes, CancellationToken cancellationToken);
}
