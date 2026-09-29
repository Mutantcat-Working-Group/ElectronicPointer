using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Platform.Recognition;

/// <summary>
/// Reads ink and hands back the geometry each stroke was probably meant to be.
///
/// The contract is deliberately about tidying drawn shapes rather than transcribing
/// handwriting: that is what lets one implementation serve Windows, macOS and Linux from
/// the same source, where the old Windows Ink analyser could only ever run on Windows.
/// </summary>
public interface IInkRecognizer
{
    bool IsSupported { get; }

    /// <summary>
    /// The strokes to read, in the order they were drawn and in board coordinates. Bad
    /// input is never an error: ink that does not read as a shape is simply absent from
    /// the report, which means "leave this stroke exactly as drawn".
    /// </summary>
    Task<RecognitionReport> RecognizeAsync(
        IReadOnlyList<IReadOnlyList<Vec2>> strokes,
        CancellationToken cancellationToken);
}
