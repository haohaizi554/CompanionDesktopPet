namespace CompanionDesktopPet.Models;

public readonly record struct ScreenPoint(double X, double Y);

public readonly record struct ScreenSize(double Width, double Height);

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool Contains(ScreenPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}
