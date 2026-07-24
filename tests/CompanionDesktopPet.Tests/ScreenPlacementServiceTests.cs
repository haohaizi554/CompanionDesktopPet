using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class ScreenPlacementServiceTests
{
    [Theory]
    [InlineData(250, 85, 270)]
    [InlineData(320, 50, 200)]
    [InlineData(390, 15, 130)]
    public void ClampVisibleBounds_TopEdge_AllowsTransparentWindowAboveWorkArea(
        double characterSize,
        double characterLeft,
        double characterTop)
    {
        var screens = new[] { new ScreenRect(0, 0, 1920, 1040) };
        var characterBounds = new ScreenRect(
            characterLeft,
            characterTop,
            characterSize,
            characterSize);

        var actual = ScreenPlacementService.ClampVisibleBounds(
            new ScreenPoint(700, -900),
            characterBounds,
            screens);

        Assert.Equal(-characterTop, actual.Y);
        Assert.Equal(700, actual.X);
    }

    [Fact]
    public void ClampVisibleBounds_BottomEdge_ConstrainsTheCharacterNotTransparentWindow()
    {
        var screens = new[] { new ScreenRect(0, 0, 1920, 1040) };
        var characterBounds = new ScreenRect(50, 200, 320, 320);

        var actual = ScreenPlacementService.ClampVisibleBounds(
            new ScreenPoint(700, 1000),
            characterBounds,
            screens);

        Assert.Equal(520, actual.Y);
    }

    [Fact]
    public void ClampVisibleBounds_UsesCharacterCenterToChooseAMonitor()
    {
        var screens = new[]
        {
            new ScreenRect(-1280, -200, 1280, 1024),
            new ScreenRect(0, 0, 1920, 1040)
        };
        var characterBounds = new ScreenRect(50, 200, 320, 320);

        var actual = ScreenPlacementService.ClampVisibleBounds(
            new ScreenPoint(-1250, -1000),
            characterBounds,
            screens);

        Assert.Equal(-400, actual.Y);
        Assert.Equal(-1250, actual.X);
    }

    [Fact]
    public void ClampVisibleBounds_OversizedCharacterPinsItsTopLeftVisibleCorner()
    {
        var screens = new[] { new ScreenRect(10, 20, 200, 100) };

        var actual = ScreenPlacementService.ClampVisibleBounds(
            new ScreenPoint(500, 500),
            new ScreenRect(30, 40, 300, 200),
            screens);

        Assert.Equal(new ScreenPoint(-20, -20), actual);
    }

    [Fact]
    public void PlaceGrabbedVisibleBounds_PreservesTheGrabPointAndClampsAtTheTop()
    {
        var characterBounds = new ScreenRect(50, 200, 320, 320);
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1040) };

        var free = ScreenPlacementService.PlaceGrabbedVisibleBounds(
            new ScreenPoint(850, 450),
            new ScreenPoint(110, 90),
            characterBounds,
            workAreas);
        var atTop = ScreenPlacementService.PlaceGrabbedVisibleBounds(
            new ScreenPoint(850, 10),
            new ScreenPoint(110, 90),
            characterBounds,
            workAreas);

        Assert.Equal(new ScreenPoint(690, 160), free);
        Assert.Equal(new ScreenPoint(690, -200), atTop);
    }

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
