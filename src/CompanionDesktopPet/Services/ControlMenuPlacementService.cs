using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public enum ControlMenuSide
{
    Left,
    Right,
    Above,
    Below
}

public readonly record struct ControlMenuPlacement(
    ScreenPoint Origin,
    ControlMenuSide Side,
    bool SubmenuOpensRight);

public static class ControlMenuPlacementService
{
    public const double GapDips = 16;

    public static ControlMenuPlacement Place(
        ScreenRect character,
        ScreenSize menu,
        ScreenRect workArea)
    {
        var menuWidth = Math.Max(0, menu.Width);
        var menuHeight = Math.Max(0, menu.Height);
        var spaceLeft = character.Left - workArea.Left;
        var spaceRight = workArea.Right - character.Right;
        var spaceAbove = character.Top - workArea.Top;
        var spaceBelow = workArea.Bottom - character.Bottom;
        var requiredWidth = menuWidth + GapDips;
        var requiredHeight = menuHeight + GapDips;
        var fitsLeft = spaceLeft >= requiredWidth;
        var fitsRight = spaceRight >= requiredWidth;
        var fitsAbove = spaceAbove >= requiredHeight;
        var fitsBelow = spaceBelow >= requiredHeight;

        var side = ResolveSide(
            fitsLeft,
            fitsRight,
            fitsAbove,
            fitsBelow,
            spaceLeft,
            spaceRight,
            spaceAbove,
            spaceBelow);

        double x;
        double y;
        switch (side)
        {
            case ControlMenuSide.Left:
                x = character.Left - GapDips - menuWidth;
                y = Align(character.Top, character.Height, menuHeight, workArea.Top, workArea.Bottom);
                break;
            case ControlMenuSide.Right:
                x = character.Right + GapDips;
                y = Align(character.Top, character.Height, menuHeight, workArea.Top, workArea.Bottom);
                break;
            case ControlMenuSide.Above:
                x = Align(character.Left, character.Width, menuWidth, workArea.Left, workArea.Right);
                y = character.Top - GapDips - menuHeight;
                break;
            default:
                x = Align(character.Left, character.Width, menuWidth, workArea.Left, workArea.Right);
                y = character.Bottom + GapDips;
                break;
        }

        var maximumX = Math.Max(workArea.Left, workArea.Right - menuWidth);
        var maximumY = Math.Max(workArea.Top, workArea.Bottom - menuHeight);
        return new ControlMenuPlacement(
            new ScreenPoint(
                Math.Clamp(x, workArea.Left, maximumX),
                Math.Clamp(y, workArea.Top, maximumY)),
            side,
            SubmenuOpensRight(side, spaceLeft, spaceRight));
    }

    private static ControlMenuSide ResolveSide(
        bool fitsLeft,
        bool fitsRight,
        bool fitsAbove,
        bool fitsBelow,
        double spaceLeft,
        double spaceRight,
        double spaceAbove,
        double spaceBelow)
    {
        if (fitsLeft || fitsRight)
        {
            if (fitsLeft && (!fitsRight || spaceLeft >= spaceRight))
            {
                return ControlMenuSide.Left;
            }

            return ControlMenuSide.Right;
        }

        if (fitsAbove || fitsBelow)
        {
            if (fitsAbove && (!fitsBelow || spaceAbove >= spaceBelow))
            {
                return ControlMenuSide.Above;
            }

            return ControlMenuSide.Below;
        }

        var horizontal = Math.Max(spaceLeft, spaceRight);
        var vertical = Math.Max(spaceAbove, spaceBelow);
        if (horizontal >= vertical)
        {
            return spaceLeft >= spaceRight ? ControlMenuSide.Left : ControlMenuSide.Right;
        }

        return spaceAbove >= spaceBelow ? ControlMenuSide.Above : ControlMenuSide.Below;
    }

    private static bool SubmenuOpensRight(
        ControlMenuSide side,
        double spaceLeft,
        double spaceRight) =>
        side switch
        {
            ControlMenuSide.Left => false,
            ControlMenuSide.Right => true,
            _ => spaceRight >= spaceLeft
        };

    private static double Align(
        double anchor,
        double anchorLength,
        double popupLength,
        double areaStart,
        double areaEnd)
    {
        var desired = anchor + ((anchorLength - popupLength) / 2);
        var maximum = Math.Max(areaStart, areaEnd - popupLength);
        return Math.Clamp(desired, areaStart, maximum);
    }
}
