using SkiaSharp;
using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Rendering;

/// <summary>
/// Everything the renderer needs that is not part of the board itself: the surface size,
/// the backdrop the ink sits on, and the optional chrome drawn around the ink.
/// </summary>
public sealed record RenderOptions
{
    public static readonly RenderOptions Default = new();

    /// <summary>Transparent, matching a click-through overlay over the desktop.</summary>
    public SKColor Background { get; init; } = SkiaColorExtensions.Transparent;

    public float Width { get; init; }

    public float Height { get; init; }

    /// <summary>Device pixel ratio, so 1 board unit maps to this many device pixels.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>
    /// Where the top-left corner of this surface sits in board coordinates. The default is
    /// the board origin itself, which is what an offscreen export wants. An overlay window
    /// occupying the right half of a desktop passes that half's offset, so a stroke drawn
    /// there stays at its place on the desktop instead of sliding onto the first screen.
    /// </summary>
    public Vec2 Origin { get; init; } = Vec2.Zero;

    /// <summary>Draws the rubber-band rectangle shown while selecting an area.</summary>
    public SKRect? Marquee { get; init; }

    /// <summary>Draws the eraser cursor ring in board coordinates.</summary>
    public SKPoint? EraserCursor { get; init; }

    public float EraserRadius { get; init; }

    public SKColor Accent { get; init; } = new(0x0B, 0x6C, 0xD4);

    public RenderOptions WithSize(float width, float height) => this with { Width = width, Height = height };
}
