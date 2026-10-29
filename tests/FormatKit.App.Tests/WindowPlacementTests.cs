using System.Windows;

namespace FormatKit.App.Tests;

public sealed class WindowPlacementTests
{
    private static readonly Size Requested = new(1000, 650);
    private static readonly Size Minimum = new(720, 480);

    [Fact]
    public void FitsInTheWorkArea_KeepsTheRequestedSize()
    {
        Assert.Equal(Requested, WindowPlacement.Fit(Requested, new Rect(0, 0, 1920, 1040), Minimum));
    }

    [Fact]
    public void LargerThanTheWorkArea_ShrinksToIt()
    {
        // A 1024x768 screen with the taskbar (seen in VM scenario 1).
        Assert.Equal(new Size(1000, 650), WindowPlacement.Fit(Requested, new Rect(0, 0, 1024, 728), Minimum));
        Assert.Equal(new Size(800, 560), WindowPlacement.Fit(Requested, new Rect(0, 0, 800, 560), Minimum));
    }

    [Fact]
    public void WorkAreaBelowTheMinimum_KeepsTheMinimum()
    {
        Assert.Equal(Minimum, WindowPlacement.Fit(Requested, new Rect(0, 0, 640, 400), Minimum));
    }
}
