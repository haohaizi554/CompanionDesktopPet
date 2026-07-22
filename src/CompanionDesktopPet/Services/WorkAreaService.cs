using System.Windows;
using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public static class WorkAreaService
{
    public static IReadOnlyList<ScreenRect> GetWorkAreas()
    {
        var workArea = SystemParameters.WorkArea;
        return
        [
            new ScreenRect(
                workArea.Left,
                workArea.Top,
                workArea.Width,
                workArea.Height)
        ];
    }
}
