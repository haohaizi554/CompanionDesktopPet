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
}
