using Mutantcat.ElectronicPointer.Core.Ink;

namespace Mutantcat.ElectronicPointer.Core.Board;

/// <summary>
/// One whiteboard page. Holds completed strokes plus the stroke currently being drawn,
/// so the renderer can show live ink without any special casing.
/// </summary>
public sealed class BoardPage
{
    private const uint TransparentBackground = 0x00000000;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "页面";

    public uint BackgroundColor { get; set; } = TransparentBackground;

    public List<Stroke> Strokes { get; } = new();

    /// <summary>Frozen screen image sitting behind the ink, if the page was snapshotted.</summary>
    public BoardBackground? BackgroundImage { get; set; }

    /// <summary>The stroke under the pen, or null when the pen is up.</summary>
    public Stroke? ActiveStroke { get; set; }

    /// <summary>Bumped whenever anything on this page changes.</summary>
    public int Revision
    {
        get;
        private set;
    }

    public int StrokeCount => Strokes.Count;

    public void Touch() => Revision++;

    public BoardDocument? Owner
    {
        get;
        internal set;
    }

    public bool HasInk()
    {
        foreach (var stroke in Strokes)
        {
            if (stroke.Samples.Count > 0)
                return true;
        }

        return false;
    }
}
