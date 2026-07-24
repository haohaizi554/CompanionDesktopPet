using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public enum BubblePlacementSide
{
    Above,
    Below
}

public readonly record struct BubblePlacement(
    ScreenPoint Origin,
    BubblePlacementSide Side,
    double ArrowCenterX);

public static class BubblePlacementService
{
    public const double GapDips = 30;
    public const double ReturnHysteresisDips = 12;
    public const double ArrowEdgeInsetDips = 32;

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
        var aboveFits = spaceAbove >= requiredSpace;
        var belowFits = spaceBelow >= requiredSpace;
        var side = ResolveSide(
            previousSide,
            aboveFits,
            belowFits,
            spaceAbove,
            spaceBelow,
            requiredSpace,
            hysteresis);

        var desiredX = character.Left + ((character.Width - bubble.Width) / 2);
        var maximumX = Math.Max(workArea.Left, workArea.Right - bubble.Width);
        var x = Math.Clamp(desiredX, workArea.Left, maximumX);
        var desiredY = side == BubblePlacementSide.Above
            ? character.Top - gap - bubble.Height
            : character.Bottom + gap;
        var maximumY = Math.Max(workArea.Top, workArea.Bottom - bubble.Height);
        var y = Math.Clamp(desiredY, workArea.Top, maximumY);
        var arrowInset = Math.Min(bubble.Width / 2, ArrowEdgeInsetDips * unitsPerDip);
        var arrowCenterX = Math.Clamp(
            character.Left + (character.Width / 2) - x,
            arrowInset,
            Math.Max(arrowInset, bubble.Width - arrowInset));
        return new BubblePlacement(new ScreenPoint(x, y), side, arrowCenterX);
    }

    private static BubblePlacementSide ResolveSide(
        BubblePlacementSide previousSide,
        bool aboveFits,
        bool belowFits,
        double spaceAbove,
        double spaceBelow,
        double requiredSpace,
        double hysteresis)
    {
        if (aboveFits && belowFits)
        {
            return previousSide == BubblePlacementSide.Below
                   && spaceAbove < requiredSpace + hysteresis
                ? BubblePlacementSide.Below
                : BubblePlacementSide.Above;
        }

        if (aboveFits)
        {
            return BubblePlacementSide.Above;
        }

        if (belowFits)
        {
            return BubblePlacementSide.Below;
        }

        if (Math.Abs(spaceAbove - spaceBelow) < double.Epsilon)
        {
            return previousSide;
        }

        return spaceAbove > spaceBelow
            ? BubblePlacementSide.Above
            : BubblePlacementSide.Below;
    }
}
