using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Recognition;

namespace Mutantcat.ElectronicPointer.Platform.Recognition;

/// <summary>
/// The recognizer that ships with every platform. The old build asked Windows Ink to
/// analyse the ink and had nothing at all to fall back on elsewhere, so the feature simply
/// did not exist outside Windows; this one runs the same engine from the core assembly on
/// all three desktops, which is why <see cref="IsSupported"/> never says no.
/// </summary>
public sealed class BuiltInShapeRecognizer : IInkRecognizer
{
    public bool IsSupported => true;

    public Task<RecognitionReport> RecognizeAsync(
        IReadOnlyList<IReadOnlyList<Vec2>> strokes,
        CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                var read = new List<RecognizedInk>();

                for (var index = 0; index < strokes.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var shape = ShapeRecognizer.Recognize(strokes[index]);
                    if (shape.IsRecognized)
                        read.Add(new RecognizedInk(index, shape));
                }

                return new RecognitionReport(read, Summarise(read.Count, strokes.Count));
            },
            cancellationToken);

    private static string Summarise(int tidied, int total) => total switch
    {
        0 => "没有选中的笔迹",
        _ when tidied == 0 => $"没有识别到形状，{total} 笔墨迹保持原样",
        _ when tidied == total => $"已整理 {tidied} 个形状",
        _ => $"已整理 {tidied} 个形状，其余 {total - tidied} 笔保持原样",
    };
}
