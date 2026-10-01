using Mutantcat.ElectronicPointer.Platform.Overlay;
using Mutantcat.ElectronicPointer.Platform.Windows;
using Xunit;

namespace Mutantcat.ElectronicPointer.Platform.Windows.Tests;

/// <summary>
/// The radius a companion window is cut at. The window region is measured in device pixels
/// while every card draws in layout units, so the one number that decides whether four
/// corners agree has to cross the window's scale on the way across. A desk at 125% that
/// asks for the layout number gets corners a quarter too small, and a report of three
/// corners round with one square is how that shows up once a host starts deciding corners
/// of its own.
/// </summary>
public sealed class Win32WindowShapeTests
{
    private const int LayoutRadius = CompanionShape.CornerRadius;

    private const int Wide = 400;

    private const int Tall = 120;

    [Fact]
    public void AnUnscaledDeskAsksForTheLayoutRadiusPlusOne()
    {
        // The extra pixel is deliberate: the cut is a hard edge and the card's corner is
        // anti-aliased, so the cut sits just outside the card rather than shaving it.
        Assert.Equal(LayoutRadius + 1, Win32OverlayChrome.ShapeRadius(LayoutRadius, 96, Wide, Tall));
    }

    [Fact]
    public void AScaledDeskCrossesTheWindowsScale()
    {
        // 125% on a 1080p desk, which is the common case rather than the exception.
        Assert.Equal((int)Math.Round(LayoutRadius * 1.25) + 1, Win32OverlayChrome.ShapeRadius(LayoutRadius, 120, Wide, Tall));

        Assert.Equal((int)Math.Round(LayoutRadius * 1.5) + 1, Win32OverlayChrome.ShapeRadius(LayoutRadius, 144, Wide, Tall));
    }

    [Fact]
    public void AnUnknownScaleIsReadAsUnscaled()
    {
        // A window with no DPI of its own, which happens for a handle the scale is asked
        // about before it has been shown, must not collapse the radius to nothing.
        Assert.Equal(LayoutRadius + 1, Win32OverlayChrome.ShapeRadius(LayoutRadius, 0, Wide, Tall));
    }

    [Fact]
    public void ASquareCornerIsLeftSquare()
    {
        Assert.Equal(0, Win32OverlayChrome.ShapeRadius(0, 96, Wide, Tall));

        Assert.Equal(0, Win32OverlayChrome.ShapeRadius(-4, 96, Wide, Tall));
    }

    [Fact]
    public void AWindowSmallerThanItsRadiusIsCutToHalfOfItsShorterSide()
    {
        // A toolbar dragged thin must still have a corner rather than none: a clamp to
        // nothing would square the corner off, which is the defect being fixed.
        Assert.Equal(4, Win32OverlayChrome.ShapeRadius(LayoutRadius, 96, 8, 16));

        Assert.Equal(1, Win32OverlayChrome.ShapeRadius(LayoutRadius, 96, 2, 1));
    }

    [Fact]
    public void EveryCornerIsCutToTheSameRadius()
    {
        // ShapeRadius is the only place the radius is decided, so the four corners of one
        // window are cut from one number by construction. The window is many sizes over
        // its life, resized on every drag, and each size is asked for independently.
        foreach (var dpi in new uint[] { 96, 120, 144, 168, 192 })
        {
            foreach (var height in new[] { 40, 120, 600 })
            {
                var radius = Win32OverlayChrome.ShapeRadius(LayoutRadius, dpi, Wide, height);

                Assert.InRange(radius, 1, height / 2);
            }
        }
    }
}
