using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class WorkAreaServiceTests
{
    [Fact]
    public void GetWorkAreas_ReturnsAtLeastOnePositiveArea()
    {
        var areas = WorkAreaService.GetWorkAreas();

        Assert.NotEmpty(areas);
        Assert.All(areas, area =>
        {
            Assert.True(area.Width > 0);
            Assert.True(area.Height > 0);
        });
    }

    [Fact]
    public void GetWorkAreas_PrimaryAreaUsesWpfLogicalCoordinates()
    {
        var expected = System.Windows.SystemParameters.WorkArea;

        var actual = WorkAreaService.GetWorkAreas()[0];

        Assert.InRange(Math.Abs(actual.Left - expected.Left), 0, 1);
        Assert.InRange(Math.Abs(actual.Top - expected.Top), 0, 1);
        Assert.InRange(Math.Abs(actual.Width - expected.Width), 0, 1);
        Assert.InRange(Math.Abs(actual.Height - expected.Height), 0, 1);
    }
}
