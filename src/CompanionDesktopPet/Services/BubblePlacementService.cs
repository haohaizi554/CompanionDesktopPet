using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public enum BubblePlacementSide
{
    Above,
    Below
}

public readonly record struct BubblePlacement(
    ScreenPoint Origin,
    BubblePlacementSide Side);

public static class BubblePlacementService
{
    public const double GapDips = 30;
    public const double ReturnHysteresisDips = 12;

    public static BubblePlacement Place(
        ScreenRect character,
        ScreenSize bubble,
        ScreenRect workArea,
        BubblePlacementSide previousSide,
        double unitsPerDip = 1)
    {
        var gap = GapDips * unitsPerDip;
        var hysteresis = ReturnHysteresisDips * unitsPerDip;
        var requiredSpace = bubble.Height + gap;
        var spaceAbove = character.Top - workArea.Top;
        var spaceBelow = workArea.Bottom - character.Bottom;

        var side = previousSide == BubblePlacementSide.Below
            && spaceAbove < requiredSpace + hysteresis
                ? BubblePlacementSide.Below
                : spaceAbove >= requiredSpace
                    ? BubblePlacementSide.Above
                    : BubblePlacementSide.Below;

        if (side == BubblePlacementSide.Below
            && spaceBelow < requiredSpace
            && spaceAbove >= requiredSpace)
        {
            side = BubblePlacementSide.Above;
        }
        else if (side == BubblePlacementSide.Above
                 && spaceAbove < requiredSpace
                 && spaceBelow >= requiredSpace)
        {
            side = BubblePlacementSide.Below;
        }

        var desiredX = character.Left + ((character.Width - bubble.Width) / 2);
        var maximumX = Math.Max(workArea.Left, workArea.Right - bubble.Width);
        var x = Math.Clamp(desiredX, workArea.Left, maximumX);
        var y = side == BubblePlacementSide.Above
            ? character.Top - gap - bubble.Height
            : character.Bottom + gap;
        return new BubblePlacement(new ScreenPoint(x, y), side);
    }
}
