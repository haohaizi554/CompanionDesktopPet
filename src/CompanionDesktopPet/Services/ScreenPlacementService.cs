using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public static class ScreenPlacementService
{
    public static ScreenPoint Clamp(
        ScreenPoint requested,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<ScreenRect> workAreas)
    {
        if (workAreas.Count == 0)
        {
            return requested;
        }

        var area = workAreas.FirstOrDefault(screen => screen.Contains(requested));
        if (area.Width <= 0)
        {
            area = workAreas.MinBy(screen => DistanceSquared(requested, screen));
        }

        var maxX = Math.Max(area.Left, area.Right - windowWidth);
        var maxY = Math.Max(area.Top, area.Bottom - windowHeight);
        return new ScreenPoint(
            Math.Clamp(requested.X, area.Left, maxX),
            Math.Clamp(requested.Y, area.Top, maxY));
    }

    private static double DistanceSquared(ScreenPoint point, ScreenRect screen)
    {
        var x = Math.Clamp(point.X, screen.Left, screen.Right);
        var y = Math.Clamp(point.Y, screen.Top, screen.Bottom);
        return Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2);
    }
}
