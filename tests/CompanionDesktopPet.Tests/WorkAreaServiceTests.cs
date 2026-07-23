using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using FormsScreen = System.Windows.Forms.Screen;

namespace CompanionDesktopPet.Tests;

public sealed class WorkAreaServiceTests
{
    [Fact]
    public void MapPixelWorkAreas_UsesPrimaryScaleAndMovesPrimaryToIndexZero()
    {
        var pixelAreas = new[]
        {
            new ScreenRect(-1280, -100, 1280, 1024),
            new ScreenRect(0, 0, 1920, 1040),
            new ScreenRect(1920, 0, 2560, 1400)
        };
        var primaryLogical = new ScreenRect(0, 0, 1536, 832);

        var mapped = WorkAreaService.MapPixelWorkAreas(
            pixelAreas,
            primaryIndex: 1,
            primaryLogical);

        Assert.Equal(3, mapped.Count);
        Assert.Equal(primaryLogical, mapped[0]);
        AssertRect(new ScreenRect(-1024, -80, 1024, 819.2), mapped[1]);
        AssertRect(new ScreenRect(1536, 0, 2048, 1120), mapped[2]);
    }

    [Fact]
    public void MapPixelWorkAreas_EmptyInputFallsBackToPrimaryLogicalArea()
    {
        var primary = new ScreenRect(5, 7, 1200, 800);

        var mapped = WorkAreaService.MapPixelWorkAreas([], 0, primary);

        Assert.Equal([primary], mapped);
    }

    [Fact]
    public void GetWorkAreas_ReturnsEveryConnectedScreen()
    {
        var screens = FormsScreen.AllScreens;
        var areas = WorkAreaService.GetWorkAreas();

        Assert.Equal(Math.Max(1, screens.Length), areas.Count);
        Assert.All(areas, area =>
        {
            Assert.True(area.Width > 0);
            Assert.True(area.Height > 0);
        });
    }

    [Fact]
    public void GetWorkAreas_PrimaryAreaUsesWpfLogicalCoordinatesAtIndexZero()
    {
        var expected = System.Windows.SystemParameters.WorkArea;

        var actual = WorkAreaService.GetWorkAreas()[0];

        AssertRect(
            new ScreenRect(expected.Left, expected.Top, expected.Width, expected.Height),
            actual);
    }

    [Fact]
    public void GetWorkAreas_MatchesPureMappingOfAllWindowsScreens()
    {
        var screens = FormsScreen.AllScreens;
        if (screens.Length == 0)
        {
            Assert.Single(WorkAreaService.GetWorkAreas());
            return;
        }

        var primaryIndex = Array.FindIndex(screens, screen => screen.Primary);
        if (primaryIndex < 0)
        {
            primaryIndex = 0;
        }

        var expected = WorkAreaService.MapPixelWorkAreas(
            screens.Select(screen => new ScreenRect(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height)).ToArray(),
            primaryIndex,
            new ScreenRect(
                System.Windows.SystemParameters.WorkArea.Left,
                System.Windows.SystemParameters.WorkArea.Top,
                System.Windows.SystemParameters.WorkArea.Width,
                System.Windows.SystemParameters.WorkArea.Height));

        var actual = WorkAreaService.GetWorkAreas();

        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            AssertRect(expected[index], actual[index]);
        }
    }

    private static void AssertRect(ScreenRect expected, ScreenRect actual)
    {
        Assert.InRange(Math.Abs(actual.Left - expected.Left), 0, 0.01);
        Assert.InRange(Math.Abs(actual.Top - expected.Top), 0, 0.01);
        Assert.InRange(Math.Abs(actual.Width - expected.Width), 0, 0.01);
        Assert.InRange(Math.Abs(actual.Height - expected.Height), 0, 0.01);
    }
}
