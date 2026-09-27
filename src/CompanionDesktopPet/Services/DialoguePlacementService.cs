using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public enum DialoguePlacementSide
{
    Above,
    Below,
    Left,
    Right
}

public readonly record struct DialoguePlacement(
    ScreenPoint Origin,
    DialoguePlacementSide Side);

public static class DialoguePlacementService
{
    public static DialoguePlacement Place(
        ScreenRect character,
        ScreenSize panel,
        ScreenRect workArea)
    {
        foreach (var side in new[]
                 {
                     DialoguePlacementSide.Below,
                     DialoguePlacementSide.Left,
                     DialoguePlacementSide.Right,
                     DialoguePlacementSide.Above
                 })
        {
            if (TryPlace(side, character, panel, workArea, out var placement))
            {
                return placement;
            }
        }

        return Clamp(DialoguePlacementSide.Below, character, panel, workArea);
    }

    private static bool TryPlace(
        DialoguePlacementSide side,
        ScreenRect character,
        ScreenSize panel,
        ScreenRect workArea,
        out DialoguePlacement placement)
    {
        placement = Clamp(side, character, panel, workArea);
        var rect = new ScreenRect(placement.Origin.X, placement.Origin.Y, panel.Width, panel.Height);
        if (!Fits(workArea, rect) || Intersects(rect, character))
        {
            return false;
        }

        return StaysOnSide(side, rect, character);
    }

    private static DialoguePlacement Clamp(
        DialoguePlacementSide side,
        ScreenRect character,
        ScreenSize panel,
        ScreenRect workArea)
    {
        var gap = BubblePlacementService.GapDips;
        double x;
        double y;
        switch (side)
        {
            case DialoguePlacementSide.Below:
                x = character.Left + ((character.Width - panel.Width) / 2);
                y = character.Bottom + gap;
                break;
            case DialoguePlacementSide.Left:
                x = character.Left - gap - panel.Width;
                y = character.Top + ((character.Height - panel.Height) / 2);
                break;
            case DialoguePlacementSide.Right:
                x = character.Right + gap;
                y = character.Top + ((character.Height - panel.Height) / 2);
                break;
            default:
                x = character.Left + ((character.Width - panel.Width) / 2);
                y = character.Top - gap - panel.Height;
                break;
        }

        var maximumX = Math.Max(workArea.Left, workArea.Right - panel.Width);
        var maximumY = Math.Max(workArea.Top, workArea.Bottom - panel.Height);
        return new DialoguePlacement(
            new ScreenPoint(Math.Clamp(x, workArea.Left, maximumX), Math.Clamp(y, workArea.Top, maximumY)),
            side);
    }

    private static bool StaysOnSide(DialoguePlacementSide side, ScreenRect rect, ScreenRect character) =>
        side switch
        {
            DialoguePlacementSide.Above => rect.Bottom <= character.Top + 0.5,
            DialoguePlacementSide.Below => rect.Top >= character.Bottom - 0.5,
            DialoguePlacementSide.Left => rect.Right <= character.Left + 0.5,
            _ => rect.Left >= character.Right - 0.5
        };

    private static bool Fits(ScreenRect workArea, ScreenRect rect) =>
        rect.Left >= workArea.Left - 0.5
        && rect.Top >= workArea.Top - 0.5
        && rect.Right <= workArea.Right + 0.5
        && rect.Bottom <= workArea.Bottom + 0.5;

    private static bool Intersects(ScreenRect left, ScreenRect right) =>
        left.Left < right.Right
        && left.Right > right.Left
        && left.Top < right.Bottom
        && left.Bottom > right.Top;
}
