using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public static class ScreenPlacementService
{
    public static ScreenPoint PlaceGrabbedVisibleBounds(
        ScreenPoint pointer,
        ScreenPoint grabOffsetWithinVisibleBounds,
        ScreenRect localVisibleBounds,
        IReadOnlyList<ScreenRect> workAreas)
    {
        var requestedWindowOrigin = new ScreenPoint(
            pointer.X - grabOffsetWithinVisibleBounds.X - localVisibleBounds.Left,
            pointer.Y - grabOffsetWithinVisibleBounds.Y - localVisibleBounds.Top);
        return ClampVisibleBounds(requestedWindowOrigin, localVisibleBounds, workAreas);
    }

    public static ScreenPoint ClampVisibleBounds(
        ScreenPoint requestedWindowOrigin,
        ScreenRect localVisibleBounds,
        IReadOnlyList<ScreenRect> workAreas)
    {
        if (workAreas.Count == 0)
        {
            return requestedWindowOrigin;
        }

        var requestedCenter = new ScreenPoint(
            requestedWindowOrigin.X + localVisibleBounds.Left + (localVisibleBounds.Width / 2),
            requestedWindowOrigin.Y + localVisibleBounds.Top + (localVisibleBounds.Height / 2));
        var area = workAreas.FirstOrDefault(screen => screen.Contains(requestedCenter));
        if (area.Width <= 0)
        {
            area = workAreas.MinBy(screen => DistanceSquared(requestedCenter, screen));
        }

        var visibleWidth = Math.Max(0, localVisibleBounds.Width);
        var visibleHeight = Math.Max(0, localVisibleBounds.Height);
        var minX = area.Left - localVisibleBounds.Left;
        var minY = area.Top - localVisibleBounds.Top;
        var maxX = visibleWidth >= area.Width
            ? minX
            : area.Right - localVisibleBounds.Right;
        var maxY = visibleHeight >= area.Height
            ? minY
            : area.Bottom - localVisibleBounds.Bottom;
        return new ScreenPoint(
            Math.Clamp(requestedWindowOrigin.X, minX, maxX),
            Math.Clamp(requestedWindowOrigin.Y, minY, maxY));
    }

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
