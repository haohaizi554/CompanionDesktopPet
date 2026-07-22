using CompanionDesktopPet.Models;
using Forms = System.Windows.Forms;

namespace CompanionDesktopPet.Services;

public static class WorkAreaService
{
    public static IReadOnlyList<ScreenRect> GetWorkAreas() =>
        Forms.Screen.AllScreens
            .Select(screen => screen.WorkingArea)
            .Select(area => new ScreenRect(area.Left, area.Top, area.Width, area.Height))
            .ToArray();
}
