using GameLauncher.Core.Settings;

namespace GameLauncher.Core.Tests;

public sealed class WindowPlacementTests
{
    private static readonly PixelRect FullHd = new(0, 0, 1920, 1040);

    [Fact]
    public void FitInto_WindowInsideArea_Unchanged()
    {
        Assert.Equal(new PixelRect(100, 50, 1200, 800), new WindowPlacement(100, 50, 1200, 800, false).FitInto(FullHd));
    }

    [Fact]
    public void FitInto_TooBigAfterResolutionChange_ShrinksToArea()
    {
        Assert.Equal(new PixelRect(0, 0, 1920, 1040), new WindowPlacement(-10, -10, 2560, 1400, false).FitInto(FullHd));
    }

    [Fact]
    public void FitInto_PartlyOffScreen_MovesInside()
    {
        Assert.Equal(new PixelRect(720, 240, 1200, 800), new WindowPlacement(1500, 900, 1200, 800, false).FitInto(FullHd));
    }

    [Fact]
    public void FitInto_TooSmall_GrowsToMinimum()
    {
        var fitted = new WindowPlacement(10, 10, 100, 50, false).FitInto(FullHd);

        Assert.Equal(WindowPlacement.MinWidth, fitted.Width);
        Assert.Equal(WindowPlacement.MinHeight, fitted.Height);
    }

    [Fact]
    public void FitInto_SecondMonitorWithOffset_StaysOnIt()
    {
        var right = new PixelRect(1920, 0, 2560, 1400);

        Assert.Equal(new PixelRect(2000, 100, 1200, 800), new WindowPlacement(2000, 100, 1200, 800, true).FitInto(right));
    }
}
