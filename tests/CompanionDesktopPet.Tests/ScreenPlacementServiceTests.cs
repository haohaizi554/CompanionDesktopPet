using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class ScreenPlacementServiceTests
{
    [Fact]
    public void Clamp_OffScreenPosition_ReturnsVisiblePoint()
    {
        var screens = new[] { new ScreenRect(0, 0, 1920, 1040) };

        var actual = ScreenPlacementService.Clamp(new ScreenPoint(5000, -800), 420, 500, screens);

        Assert.Equal(new ScreenPoint(1500, 0), actual);
    }

    [Fact]
    public void Clamp_ValidPosition_IsUnchanged()
    {
        var screens = new[] { new ScreenRect(0, 0, 1920, 1040) };

        var actual = ScreenPlacementService.Clamp(new ScreenPoint(900, 400), 420, 500, screens);

        Assert.Equal(new ScreenPoint(900, 400), actual);
    }

    [Fact]
    public void Clamp_NoScreens_PreservesRequestedPoint()
    {
        var requested = new ScreenPoint(900, 400);

        var actual = ScreenPlacementService.Clamp(requested, 420, 500, []);

        Assert.Equal(requested, actual);
    }
}
