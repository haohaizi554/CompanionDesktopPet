using System.Windows;
using CompanionDesktopPet.Models;
using FormsScreen = System.Windows.Forms.Screen;

namespace CompanionDesktopPet.Services;

public static class WorkAreaService
{
    public static IReadOnlyList<ScreenRect> GetWorkAreas()
    {
        var wpfArea = SystemParameters.WorkArea;
        var primaryLogical = new ScreenRect(
            wpfArea.Left,
            wpfArea.Top,
            wpfArea.Width,
            wpfArea.Height);
        var screens = FormsScreen.AllScreens;
        if (screens.Length == 0)
        {
            return [primaryLogical];
        }

        var primaryIndex = Array.FindIndex(screens, screen => screen.Primary);
        if (primaryIndex < 0)
        {
            primaryIndex = 0;
        }

        var pixelAreas = screens
            .Select(screen => new ScreenRect(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height))
            .ToArray();
        return MapPixelWorkAreas(pixelAreas, primaryIndex, primaryLogical);
    }

    public static IReadOnlyList<ScreenRect> MapPixelWorkAreas(
        IReadOnlyList<ScreenRect> pixelWorkAreas,
        int primaryIndex,
        ScreenRect primaryLogicalArea)
    {
        ArgumentNullException.ThrowIfNull(pixelWorkAreas);
        if (pixelWorkAreas.Count == 0)
        {
            return [primaryLogicalArea];
        }

        if (primaryIndex < 0 || primaryIndex >= pixelWorkAreas.Count)
        {
            primaryIndex = 0;
        }

        var primaryPixels = pixelWorkAreas[primaryIndex];
        if (!IsPositiveFinite(primaryPixels.Width)
            || !IsPositiveFinite(primaryPixels.Height)
            || !IsPositiveFinite(primaryLogicalArea.Width)
            || !IsPositiveFinite(primaryLogicalArea.Height))
        {
            return [primaryLogicalArea];
        }

        var scaleX = primaryLogicalArea.Width / primaryPixels.Width;
        var scaleY = primaryLogicalArea.Height / primaryPixels.Height;
        var result = new List<ScreenRect>(pixelWorkAreas.Count)
        {
            primaryLogicalArea
        };
        for (var index = 0; index < pixelWorkAreas.Count; index++)
        {
            if (index == primaryIndex)
            {
                continue;
            }

            var area = pixelWorkAreas[index];
            result.Add(new ScreenRect(
                primaryLogicalArea.Left + ((area.Left - primaryPixels.Left) * scaleX),
                primaryLogicalArea.Top + ((area.Top - primaryPixels.Top) * scaleY),
                area.Width * scaleX,
                area.Height * scaleY));
        }

        return result;
    }

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;
}
